﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using SonarAnalyzer.Common;
using SonarAnalyzer.Helpers;
using SonarAnalyzer.Rules.Common;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace SonarAnalyzer.Rules.VisualBasic
{
    [DiagnosticAnalyzer(LanguageNames.VisualBasic)]
    [Rule(DiagnosticId)]
    public class UseShortCircuitingOperator : UseShortCircuitingOperatorBase<SyntaxKind, BinaryExpressionSyntax>
    {
        protected static readonly DiagnosticDescriptor rule =
            DiagnosticDescriptorBuilder.GetDescriptor(DiagnosticId, MessageFormat, RspecStrings.ResourceManager);

        protected override DiagnosticDescriptor Rule => rule;

        protected override string GetSuggestedOpName(BinaryExpressionSyntax node) =>
            OperatorNames[ShortCircuitingAlternative[node.Kind()]];

        protected override string GetCurrentOpName(BinaryExpressionSyntax node) =>
            OperatorNames[node.Kind()];

        protected override IEnumerable<SyntaxNode> GetOperands(BinaryExpressionSyntax expression)
        {
            yield return expression.Left;
            yield return expression.Right;
        }

        protected override SyntaxToken GetOperator(BinaryExpressionSyntax expression) =>
            expression.OperatorToken;

        internal static readonly IDictionary<SyntaxKind, SyntaxKind> ShortCircuitingAlternative = new Dictionary<SyntaxKind, SyntaxKind>
        {
            { SyntaxKind.AndExpression, SyntaxKind.AndAlsoExpression },
            { SyntaxKind.OrExpression, SyntaxKind.OrElseExpression }
        }.ToImmutableDictionary();

        private static readonly IDictionary<SyntaxKind, string> OperatorNames = new Dictionary<SyntaxKind, string>
        {
            { SyntaxKind.AndExpression, "And" },
            { SyntaxKind.OrExpression, "Or" },
            { SyntaxKind.AndAlsoExpression, "AndAlso" },
            { SyntaxKind.OrElseExpression, "OrElse" },
        }.ToImmutableDictionary();

        protected override GeneratedCodeRecognizer GeneratedCodeRecognizer =>
             Helpers.VisualBasic.GeneratedCodeRecognizer.Instance;

        protected override ImmutableArray<SyntaxKind> SyntaxKindsOfInterest => ImmutableArray.Create<SyntaxKind>(
            SyntaxKind.AndExpression,
            SyntaxKind.OrExpression);
    }
}
