// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the MIT license.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeQuality.Analyzers.ApiDesignGuidelines;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;

namespace Microsoft.CodeQuality.CSharp.Analyzers.ApiDesignGuidelines
{
    /// <summary>
    /// CA2218: Override GetHashCode on overriding Equals
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp), Shared]
    public sealed class CSharpOverrideGetHashCodeOnOverridingEqualsFixer : OverrideGetHashCodeOnOverridingEqualsFixer
    {
        public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("CS0659");
    }
}