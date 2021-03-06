/*
 * SonarAnalyzer for .NET
 * Copyright (C) 2015-2016 SonarSource SA
 * mailto:contact@sonarsource.com
 *
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation; either
 * version 3 of the License, or (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
 * Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public
 * License along with this program; if not, write to the Free Software
 * Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02
 */

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SonarAnalyzer.Common;
using SonarAnalyzer.Helpers;
using SonarAnalyzer.Helpers.FlowAnalysis.Common;
using SonarAnalyzer.Helpers.FlowAnalysis.CSharp;

namespace SonarAnalyzer.Rules.CSharp
{
    using ExplodedGraph = Helpers.FlowAnalysis.CSharp.ExplodedGraph;

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    [Rule(DiagnosticId)]
    public class NullPointerDereference : SonarDiagnosticAnalyzer
    {
        internal const string DiagnosticId = "S2259";
        internal const string MessageFormat = "\"{0}\" is null on at least one execution path.";

        private static readonly DiagnosticDescriptor rule =
            DiagnosticDescriptorBuilder.GetDescriptor(DiagnosticId, MessageFormat, RspecStrings.ResourceManager);

        protected sealed override DiagnosticDescriptor Rule => rule;

        protected override void Initialize(SonarAnalysisContext context)
        {
            context.RegisterExplodedGraphBasedAnalysis((e, c) => CheckForNullDereference(e, c));
        }

        private static void CheckForNullDereference(ExplodedGraph explodedGraph, SyntaxNodeAnalysisContext context)
        {
            var nullPointerCheck = new NullPointerCheck(explodedGraph);
            explodedGraph.AddExplodedGraphCheck(nullPointerCheck);

            var nullIdentifiers = new HashSet<IdentifierNameSyntax>();

            EventHandler<MemberAccessedEventArgs> memberAccessedHandler =
                (sender, args) => CollectMemberAccesses(args, nullIdentifiers, context.SemanticModel);

            nullPointerCheck.MemberAccessed += memberAccessedHandler;

            try
            {
                explodedGraph.Walk();
            }
            finally
            {
                nullPointerCheck.MemberAccessed -= memberAccessedHandler;
            }

            foreach (var nullIdentifier in nullIdentifiers)
            {
                context.ReportDiagnostic(Diagnostic.Create(rule, nullIdentifier.GetLocation(), nullIdentifier.Identifier.ValueText));
            }
        }

        private static void CollectMemberAccesses(MemberAccessedEventArgs args, HashSet<IdentifierNameSyntax> nullIdentifiers,
            SemanticModel semanticModel)
        {
            if (!NullPointerCheck.IsExtensionMethod(args.Identifier.Parent, semanticModel))
            {
                nullIdentifiers.Add(args.Identifier);
            }
        }

        internal sealed class NullPointerCheck : ExplodedGraphCheck
        {
            public event EventHandler<MemberAccessedEventArgs> MemberAccessed;

            public NullPointerCheck(ExplodedGraph explodedGraph)
                : base(explodedGraph)
            {

            }

            private void OnMemberAccessed(IdentifierNameSyntax identifier)
            {
                MemberAccessed?.Invoke(this, new MemberAccessedEventArgs
                {
                    Identifier = identifier
                });
            }

            public override ProgramState PreProcessInstruction(ProgramPoint programPoint, ProgramState programState)
            {
                var instruction = programPoint.Block.Instructions[programPoint.Offset];
                switch (instruction.Kind())
                {
                    case SyntaxKind.IdentifierName:
                        return ProcessIdentifier(programPoint, programState, (IdentifierNameSyntax)instruction);

                    case SyntaxKind.AwaitExpression:
                        return ProcessAwait(programState, (AwaitExpressionSyntax)instruction);

                    case SyntaxKind.SimpleMemberAccessExpression:
                    case SyntaxKind.PointerMemberAccessExpression:
                        return ProcessMemberAccess(programState, (MemberAccessExpressionSyntax)instruction);

                    case SyntaxKind.ElementAccessExpression:
                        return ProcessElementAccess(programState, (ElementAccessExpressionSyntax)instruction);

                    default:
                        return programState;
                }
            }

            private ProgramState ProcessAwait(ProgramState programState, AwaitExpressionSyntax awaitExpression)
            {
                var identifier = awaitExpression.Expression as IdentifierNameSyntax;
                if (identifier == null)
                {
                    return programState;
                }

                var symbol = semanticModel.GetSymbolInfo(identifier).Symbol;
                return ProcessIdentifier(programState, identifier, symbol);
            }

            private ProgramState ProcessElementAccess(ProgramState programState, ElementAccessExpressionSyntax elementAccess)
            {
                var identifier = elementAccess.Expression as IdentifierNameSyntax;
                if (identifier == null)
                {
                    return programState;
                }

                var symbol = semanticModel.GetSymbolInfo(identifier).Symbol;
                if (symbol == null)
                {
                    return programState;
                }

                return ProcessIdentifier(programState, identifier, symbol);
            }

            private ProgramState ProcessMemberAccess(ProgramState programState, MemberAccessExpressionSyntax memberAccess)
            {
                var identifier = memberAccess.Expression as IdentifierNameSyntax;
                if (identifier == null)
                {
                    return programState;
                }

                var symbol = semanticModel.GetSymbolInfo(identifier).Symbol;
                if (symbol == null)
                {
                    return programState;
                }
                if ((IsNullableValueType(symbol) && !IsGetTypeCall(memberAccess)) ||
                    IsExtensionMethod(memberAccess, semanticModel))
                {
                    return programState;
                }

                return ProcessIdentifier(programState, identifier, symbol);
            }

            private static bool IsGetTypeCall(MemberAccessExpressionSyntax memberAccess)
            {
                return memberAccess.Name.Identifier.ValueText == "GetType";
            }

            private ProgramState ProcessIdentifier(ProgramPoint programPoint, ProgramState programState, IdentifierNameSyntax identifier)
            {
                if (programPoint.Block.Instructions.Last() != identifier ||
                    programPoint.Block.SuccessorBlocks.Count != 1 ||
                    (!IsSuccessorForeachBranch(programPoint) && !IsExceptionThrow(identifier)))
                {
                    return programState;
                }

                var symbol = semanticModel.GetSymbolInfo(identifier).Symbol;
                return ProcessIdentifier(programState, identifier, symbol);
            }

            private ProgramState ProcessIdentifier(ProgramState programState, IdentifierNameSyntax identifier, ISymbol symbol)
            {
                if (explodedGraph.IsLocalScoped(symbol) &&
                    symbol.HasConstraint(ObjectConstraint.Null, programState))
                {
                    OnMemberAccessed(identifier);
                    return null;
                }

                return SetNotNullConstraintOnSymbol(symbol, programState);
            }

            private static ProgramState SetNotNullConstraintOnSymbol(ISymbol symbol, ProgramState programState)
            {
                if (programState == null)
                {
                    return null;
                }

                if (symbol == null)
                {
                    return programState;
                }

                if (!IsNullableValueType(symbol))
                {
                    return symbol.SetConstraint(ObjectConstraint.NotNull, programState);
                }

                return programState;
            }

            private static bool IsNullableValueType(ISymbol symbol)
            {
                var type = symbol.GetSymbolType();
                return type.IsStruct() &&
                    type.OriginalDefinition.Is(KnownType.System_Nullable_T);
            }

            private static bool IsExceptionThrow(IdentifierNameSyntax identifier)
            {
                return identifier.GetSelfOrTopParenthesizedExpression().Parent.IsKind(SyntaxKind.ThrowStatement);
            }

            private static bool IsSuccessorForeachBranch(ProgramPoint programPoint)
            {
                var successorBlock = programPoint.Block.SuccessorBlocks.First() as BinaryBranchBlock;
                return successorBlock != null &&
                    successorBlock.BranchingNode.IsKind(SyntaxKind.ForEachStatement);
            }

            internal static bool IsExtensionMethod(SyntaxNode expression, SemanticModel semanticModel)
            {
                var memberSymbol = semanticModel.GetSymbolInfo(expression).Symbol as IMethodSymbol;
                return memberSymbol != null && memberSymbol.IsExtensionMethod;
            }
        }
    }
}