﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the MIT license.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Analyzer.Utilities;
using Analyzer.Utilities.Extensions;
using Analyzer.Utilities.FlowAnalysis.Analysis.GlobalFlowStateDictionaryAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using static Microsoft.CodeQuality.Analyzers.MicrosoftCodeQualityAnalyzersResources;

namespace Microsoft.CodeQuality.Analyzers.QualityGuidelines.AvoidMultipleEnumerations
{
    public abstract partial class AvoidMultipleEnumerations : DiagnosticAnalyzer
    {
        private const string RuleId = "CA1850";

        private static readonly DiagnosticDescriptor MultipleEnumerableDescriptor = DiagnosticDescriptorHelper.Create(
            RuleId,
            CreateLocalizableResourceString(nameof(AvoidMultipleEnumerationsTitle)),
            CreateLocalizableResourceString(nameof(AvoidMultipleEnumerationsMessage)),
            DiagnosticCategory.Performance,
            RuleLevel.Disabled,
            description: null,
            isPortedFxCopRule: false,
            isDataflowRule: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(MultipleEnumerableDescriptor);

        /// <summary>
        /// Additional types that has the ability to defer enumeration.
        /// </summary>
        private static readonly ImmutableArray<string> s_additionalDeferredTypes =
            ImmutableArray.Create(WellKnownTypeNames.SystemLinqIOrderedEnumerable1);

        /// <summary>
        /// All the immutable collections that have a conversion method from IEnumerable.
        /// </summary>
        private static readonly ImmutableArray<(string typeName, string methodName)> s_immutableCollectionsTypeNamesAndConvensionMethods = ImmutableArray.Create(
            ("System.Collections.Immutable.ImmutableArray", "ToImmutableArray"),
            ("System.Collections.Immutable.ImmutableDictionary", "ToImmutableDictionary"),
            ("System.Collections.Immutable.ImmutableHashSet", "ToImmutableHashSet"),
            ("System.Collections.Immutable.ImmutableList", "ToImmutableList"),
            ("System.Collections.Immutable.ImmutableSortedDictionary", "ToImmutableSortedDictionary"),
            ("System.Collections.Immutable.ImmutableSortedSet", "ToImmutableSortedSet"));

        /// <summary>
        /// Linq methods causing its first parameter to be enumerated.
        /// </summary>
        private static readonly ImmutableArray<string> s_linqOneParameterEnumeratedMethods = ImmutableArray.Create(
            "Aggregate",
            "All",
            "Any",
            "Average",
            "Contains",
            "Count",
            "DefaultIfEmpty",
            "ElementAt",
            "ElementAtOrDefault",
            "First",
            "FirstOrDefault",
            "Last",
            "LastOrDefault",
            "LongCount",
            "Max",
            "Min",
            "Single",
            "SingleOrDefault",
            "Sum",
            "ToArray",
            "ToDictionary",
            "ToHashSet",
            "ToList",
            "ToLookup");

        /// <summary>
        /// Linq methods causing its first two parameters to be enumerated.
        /// </summary>
        private static readonly ImmutableArray<string> s_linqTwoParametersEnumeratedMethods = ImmutableArray.Create(
            "SequenceEqual");

        /// <summary>
        /// Linq methods deferring its first parameter to be enumerated.
        /// </summary>
        private static readonly ImmutableArray<string> s_linqOneParameterDeferredMethods = ImmutableArray.Create(
            "Append",
            "AsEnumerable",
            "Cast",
            "Distinct",
            "GroupBy",
            "OfType",
            "OrderBy",
            "OrderByDescending",
            "Prepend",
            "Range",
            "Repeat",
            "Reverse",
            "Select",
            "SelectMany",
            "Skip",
            "SkipLast",
            "SkipWhile",
            "Take",
            "TakeLast",
            "ThenBy",
            "ThenByDescending",
            "Where");

        /// <summary>
        /// Linq methods deferring its first two parameters to be enumerated.
        /// </summary>
        private static readonly ImmutableArray<string> s_linqTwoParametersDeferredMethods = ImmutableArray.Create(
            "Concat",
            "Except",
            "GroupJoin",
            "Intersect",
            "Join",
            "Union");

        internal abstract GlobalFlowStateDictionaryFlowOperationVisitor CreateOperationVisitor(
            GlobalFlowStateDictionaryAnalysisContext context,
            ImmutableArray<IMethodSymbol> oneParameterDeferredMethods,
            ImmutableArray<IMethodSymbol> twoParametersDeferredMethods,
            ImmutableArray<IMethodSymbol> oneParameterEnumeratedMethods,
            ImmutableArray<IMethodSymbol> twoParametersEnumeratedMethods,
            ImmutableArray<ITypeSymbol> additionalDeferTypes,
            IMethodSymbol? getEnumeratorMethod);

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterCompilationStartAction(CompilationStartAction);
        }

        private void CompilationStartAction(CompilationStartAnalysisContext context)
        {
            context.RegisterOperationBlockStartAction(OnOperationBlockStart);
        }

        private void OnOperationBlockStart(OperationBlockStartAnalysisContext operationBlockStartAnalysisContext)
        {
            var wellKnownTypeProvider = WellKnownTypeProvider.GetOrCreate(operationBlockStartAnalysisContext.Compilation);

            var oneParameterDeferredMethods = GetOneParameterDeferredMethods(wellKnownTypeProvider);
            var twoParametersDeferredMethods = GetTwoParametersDeferredMethods(wellKnownTypeProvider);

            var oneParameterEnumeratedMethods = GetOneParameterEnumeratedMethods(wellKnownTypeProvider);
            var twoParametersEnumeratedMethods = GetTwoParametersEnumeratedMethods(wellKnownTypeProvider);

            var compilation = operationBlockStartAnalysisContext.Compilation;
            var additionalDeferTypes = GetTypes(compilation, s_additionalDeferredTypes);

            var potentialDiagnosticOperationsBuilder = ImmutableHashSet.CreateBuilder<IOperation>();
            operationBlockStartAnalysisContext.RegisterOperationAction(
                context => CollectPotentialDiagnosticOperations(
                    context,
                    oneParameterDeferredMethods,
                    twoParametersDeferredMethods,
                    oneParameterEnumeratedMethods,
                    twoParametersEnumeratedMethods,
                    additionalDeferTypes,
                    potentialDiagnosticOperationsBuilder),
                OperationKind.ParameterReference,
                OperationKind.LocalReference);

            operationBlockStartAnalysisContext.RegisterOperationBlockEndAction(
                context => Analyze(
                    context,
                    wellKnownTypeProvider,
                    oneParameterDeferredMethods,
                    twoParametersDeferredMethods,
                    oneParameterEnumeratedMethods,
                    twoParametersEnumeratedMethods,
                    additionalDeferTypes,
                    potentialDiagnosticOperationsBuilder));
        }

        private static void CollectPotentialDiagnosticOperations(
            OperationAnalysisContext context,
            ImmutableArray<IMethodSymbol> oneParameterDeferredMethods,
            ImmutableArray<IMethodSymbol> twoParametersDeferredMethods,
            ImmutableArray<IMethodSymbol> oneParameterEnumeratedMethods,
            ImmutableArray<IMethodSymbol> twoParametersEnumeratedMethods,
            ImmutableArray<ITypeSymbol> additionalDeferTypes,
            ImmutableHashSet<IOperation>.Builder builder)
        {
            var operation = context.Operation;
            if (IsDeferredType(operation.Type.OriginalDefinition, additionalDeferTypes))
            {
                var isEnumerated = IsOperationEnumeratedByMethodInvocation(operation, oneParameterDeferredMethods, twoParametersDeferredMethods, oneParameterEnumeratedMethods, twoParametersEnumeratedMethods, additionalDeferTypes)
                    || IsOperationEnumeratedByForEachLoop(operation, oneParameterDeferredMethods, twoParametersDeferredMethods, additionalDeferTypes);

                if (isEnumerated)
                    builder.Add(operation);
            }
        }

        private void Analyze(
            OperationBlockAnalysisContext context,
            WellKnownTypeProvider wellKnownTypeProvider,
            ImmutableArray<IMethodSymbol> oneParameterDeferredMethods,
            ImmutableArray<IMethodSymbol> twoParametersDeferredMethods,
            ImmutableArray<IMethodSymbol> oneParameterEnumeratedMethods,
            ImmutableArray<IMethodSymbol> twoParameterEnumeratedMethods,
            ImmutableArray<ITypeSymbol> additionalDeferTypes,
            ImmutableHashSet<IOperation>.Builder potentialDiagnosticOperationsBuilder)
        {
            var potentialDiagnosticOperations = potentialDiagnosticOperationsBuilder.ToImmutable();
            if (potentialDiagnosticOperations.IsEmpty)
            {
                return;
            }

            var cfg = context.OperationBlocks.GetControlFlowGraph();
            if (cfg == null)
            {
                return;
            }

            // In CFG blocks there is no foreach loop related Operation, so use the
            // the GetEnumerator method to find the foreach loop
            var getEnumeratorSymbol = wellKnownTypeProvider
                .GetOrCreateTypeByMetadataName(WellKnownTypeNames.SystemCollectionsGenericIEnumerable1)
                ?.GetMembers(WellKnownMemberNames.GetEnumeratorMethodName).FirstOrDefault() as IMethodSymbol;

            var analysisResult = GlobalFlowStateDictionaryAnalysis.TryGetOrComputeResult(
                cfg,
                context.OwningSymbol,
                analysisContext => CreateOperationVisitor(
                    analysisContext,
                    oneParameterDeferredMethods,
                    twoParametersDeferredMethods,
                    oneParameterEnumeratedMethods,
                    twoParameterEnumeratedMethods,
                    additionalDeferTypes,
                    getEnumeratorSymbol),
                wellKnownTypeProvider,
                context.Options,
                MultipleEnumerableDescriptor,
                pessimisticAnalysis: false);

            if (analysisResult == null)
            {
                return;
            }

            var diagnosticOperations = new HashSet<IOperation>();
            foreach (var operation in potentialDiagnosticOperations)
            {
                var result = analysisResult[operation.Kind, operation.Syntax];
                if (result.Kind != GlobalFlowStateDictionaryAnalysisValueKind.Known)
                {
                    continue;
                }

                foreach (var kvp in result.TrackedEntities)
                {
                    var trackedInvocationSet = kvp.Value;
                    if (trackedInvocationSet.EnumerationCount == InvocationCount.TwoOrMoreTime)
                    {
                        foreach (var trackedOperation in trackedInvocationSet.Operations)
                        {
                            diagnosticOperations.Add(trackedOperation);
                        }
                    }
                }
            }

            foreach (var operation in diagnosticOperations)
            {
                context.ReportDiagnostic(operation.CreateDiagnostic(MultipleEnumerableDescriptor));
            }
        }
    }
}
