﻿/*
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

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SonarAnalyzer.Common;
using SonarAnalyzer.Helpers;

namespace SonarAnalyzer.Rules.CSharp
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    [Rule(DiagnosticId)]
    public class ForeachLoopExplicitConversion : SonarDiagnosticAnalyzer
    {
        internal const string DiagnosticId = "S3217";
        internal const string MessageFormat = "Either change the type of \"{0}\" to \"{1}\" or iterate on a generic collection of type \"{2}\".";

        private static readonly DiagnosticDescriptor rule =
            DiagnosticDescriptorBuilder.GetDescriptor(DiagnosticId, MessageFormat, RspecStrings.ResourceManager);

        protected sealed override DiagnosticDescriptor Rule => rule;

        protected override void Initialize(SonarAnalysisContext context)
        {
            context.RegisterSyntaxNodeActionInNonGenerated(
                c =>
                {
                    var foreachStatement = (ForEachStatementSyntax)c.Node;
                    var foreachInfo = c.SemanticModel.GetForEachStatementInfo(foreachStatement);

                    if (foreachInfo.Equals(default(ForEachStatementInfo)) ||
                        foreachInfo.ElementConversion.IsImplicit ||
                        foreachInfo.ElementConversion.IsUserDefined ||
                        !foreachInfo.ElementConversion.Exists ||
                        foreachInfo.ElementType.Is(KnownType.System_Object))
                    {
                        return;
                    }

                    c.ReportDiagnostic(Diagnostic.Create(Rule, foreachStatement.Type.GetLocation(),
                        foreachStatement.Identifier.ValueText,
                        foreachInfo.ElementType.ToMinimalDisplayString(c.SemanticModel, foreachStatement.Type.SpanStart),
                        foreachStatement.Type.ToString()));
                },
                SyntaxKind.ForEachStatement);
        }
    }
}
