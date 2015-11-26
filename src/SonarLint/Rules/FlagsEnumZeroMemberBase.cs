﻿/*
 * SonarLint for Visual Studio
 * Copyright (C) 2015 SonarSource
 * sonarqube@googlegroups.com
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
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using SonarLint.Common;
using SonarLint.Helpers;
using System.Collections.Generic;

namespace SonarLint.Rules.Common
{
    public abstract class FlagsEnumZeroMemberBase : MultiLanguageDiagnosticAnalyzer
    {
        protected const string DiagnosticId = "S2346";
        protected const string Title = "Flags enumerations zero-value members should be named \"None\"";
        protected const string Description =
            "Consisitent use of \"None\" in flags enumerations indicates that all flag values are cleared. The value 0 should not be " +
            "used to indicate any other state, since there is no way to check that the bit 0 is set.";
        protected const string MessageFormat = "Rename \"{0}\" to \"None\".";
        protected const string Category = SonarLint.Common.Category.Naming;
        protected const Severity RuleSeverity = Severity.Minor;
        protected const bool IsActivatedByDefault = true;

        protected static readonly DiagnosticDescriptor Rule =
            new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category,
                RuleSeverity.ToDiagnosticSeverity(), IsActivatedByDefault,
                helpLinkUri: DiagnosticId.GetHelpLink(),
                description: Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(Rule); } }
    }

    public abstract class FlagsEnumZeroMemberBase<TLanguageKindEnum, TEnumDeclarationSyntax, TEnumMemberSyntax> : FlagsEnumZeroMemberBase
        where TLanguageKindEnum : struct
        where TEnumDeclarationSyntax : SyntaxNode
        where TEnumMemberSyntax : SyntaxNode
    {
        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeActionInNonGenerated(
                GeneratedCodeRecognizer,
                c =>
                {
                    var enumDeclaration = (TEnumDeclarationSyntax)c.Node;

                    var hasFlagsAttribute = FlagsEnumWithoutInitializerBase.HasFlagsAttribute(enumDeclaration, c.SemanticModel);
                    if (!hasFlagsAttribute)
                    {
                        return;
                    }
                    var zeroMember = GetZeroMember(enumDeclaration, c.SemanticModel);
                    if (zeroMember == null)
                    {
                        return;
                    }

                    var identifier = GetIdentifier(zeroMember);
                    if (identifier.ValueText != "None")
                    {
                        c.ReportDiagnostic(Diagnostic.Create(Rule, zeroMember.GetLocation(), identifier.ValueText));
                    }
                },
                SyntaxKindsOfInterest.ToArray());
        }

        protected abstract SyntaxToken GetIdentifier(TEnumMemberSyntax zeroMember);

        private TEnumMemberSyntax GetZeroMember(TEnumDeclarationSyntax node,
            SemanticModel semanticModel)
        {
            var members = GetMembers(node);

            foreach (var item in members)
            {
                var symbol = semanticModel.GetDeclaredSymbol(item) as IFieldSymbol;
                if (symbol == null)
                {
                    return null;
                }
                var constValue = symbol.ConstantValue;

                if (constValue != null)
                {
                    try
                    {
                        var v = Convert.ToInt32(constValue);
                        if (v == 0)
                        {
                            return item;
                        }
                    }
                    catch (OverflowException)
                    {
                        return null;
                    }
                }
            }
            return null;
        }
        protected abstract IEnumerable<TEnumMemberSyntax> GetMembers(TEnumDeclarationSyntax node);

        public abstract ImmutableArray<TLanguageKindEnum> SyntaxKindsOfInterest { get; }
    }
}