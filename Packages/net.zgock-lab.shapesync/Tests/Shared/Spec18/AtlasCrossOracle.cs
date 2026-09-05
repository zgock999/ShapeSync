// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

// Shared Oracle asset.
using System;
using System.Collections.Generic;

namespace zgock.ShapeSync.StackMachine.Tests.Spec18
{
    /// <summary>Checks that Layer 1--3 evidence has one consistent detached provenance context.</summary>
    internal static class AtlasCrossOracle
    {
        internal sealed class Evidence
        {
            internal Evidence(AtlasOracleEntryMetadata context, bool succeeded, StackMachineDiagnostic diagnostic, AtlasOracleEntryMetadata diagnosticContext, bool metamorphicSucceeded = true, StackMachineDiagnostic metamorphicDiagnostic = null, AtlasOracleEntryMetadata metamorphicDiagnosticContext = null)
            { Context = context; Succeeded = succeeded; Diagnostic = diagnostic; DiagnosticContext = diagnosticContext; MetamorphicSucceeded = metamorphicSucceeded; MetamorphicDiagnostic = metamorphicDiagnostic; MetamorphicDiagnosticContext = metamorphicDiagnosticContext; }
            internal AtlasOracleEntryMetadata Context { get; } internal bool Succeeded { get; } internal StackMachineDiagnostic Diagnostic { get; } internal AtlasOracleEntryMetadata DiagnosticContext { get; }
            internal bool MetamorphicSucceeded { get; } internal StackMachineDiagnostic MetamorphicDiagnostic { get; } internal AtlasOracleEntryMetadata MetamorphicDiagnosticContext { get; }
        }
        internal static bool TryValidate(AtlasOracleFixture fixture, IReadOnlyList<Evidence> evidence, out StackMachineDiagnostic diagnostic)
        {
            diagnostic=null; if(fixture==null||evidence==null||evidence.Count!=fixture.Metadata.Count) return Fail("AtlasCrossOracleInputInvalid",out diagnostic);
            var contexts=new List<AtlasOracleEntryMetadata>(); foreach(var item in evidence) { if(item==null||item.Context==null||!ResultMatchesContext(item.Succeeded,item.Diagnostic,item.DiagnosticContext,item.Context)||(item.Context.Layer==AtlasOracleLayer.Image&&!ResultMatchesContext(item.MetamorphicSucceeded,item.MetamorphicDiagnostic,item.MetamorphicDiagnosticContext,item.Context))||(item.Context.Layer!=AtlasOracleLayer.Image&&(item.MetamorphicDiagnostic!=null||item.MetamorphicDiagnosticContext!=null||!item.MetamorphicSucceeded))) return Fail("AtlasCrossOracleLayerResultInvalid",out diagnostic); contexts.Add(item.Context); }
            return TryValidate(fixture,contexts,out diagnostic);
        }
        internal static bool TryValidate(AtlasOracleFixture fixture, IReadOnlyList<AtlasOracleEntryMetadata> evidence, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (fixture == null || evidence == null || evidence.Count != fixture.Metadata.Count) return Fail("AtlasCrossOracleInputInvalid", out diagnostic);
            var expected = new Dictionary<ContextKey, AtlasOracleEntryMetadata>();
            foreach (AtlasOracleEntryMetadata entry in fixture.Metadata) expected.Add(Key(entry), entry);
            foreach (AtlasOracleEntryMetadata entry in evidence)
            {
                if (entry == null || !expected.TryGetValue(Key(entry), out AtlasOracleEntryMetadata canonical)) return Fail("AtlasCrossOracleContextMissing", out diagnostic);
                if (!Matches(canonical, entry)) return Fail("AtlasCrossOracleContextMismatch", out diagnostic);
                expected.Remove(Key(entry));
            }
            if (expected.Count != 0) return Fail("AtlasCrossOracleContextMissing", out diagnostic);
            return true;
        }

        private readonly struct ContextKey : IEquatable<ContextKey>
        {
            internal ContextKey(AtlasOracleEntryMetadata context) { MaterialId=context.MaterialId; Semantic=context.Semantic; Layer=context.Layer; }
            private MaterialId MaterialId { get; } private AtlasTextureSemantic Semantic { get; } private AtlasOracleLayer Layer { get; }
            public bool Equals(ContextKey other) => MaterialId.Equals(other.MaterialId) && Semantic==other.Semantic && Layer==other.Layer;
            public override bool Equals(object obj) => obj is ContextKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(MaterialId,Semantic,Layer);
        }
        private static ContextKey Key(AtlasOracleEntryMetadata entry) => new ContextKey(entry);
        private static bool ResultMatchesContext(bool succeeded, StackMachineDiagnostic diagnostic, AtlasOracleEntryMetadata diagnosticContext, AtlasOracleEntryMetadata context)
            => succeeded ? diagnostic == null && diagnosticContext == null : diagnostic != null && diagnostic.code == StackMachineDiagnosticCode.DomainFailure && diagnostic.domain == "atlas" && !string.IsNullOrEmpty(diagnostic.domainCode) && diagnosticContext != null && Matches(context, diagnosticContext);
        private static bool Matches(AtlasOracleEntryMetadata left, AtlasOracleEntryMetadata right)
            => left.SchemaVersion == right.SchemaVersion && left.PackingAlgorithm == right.PackingAlgorithm && left.PageExtent == right.PageExtent && left.FigureIdentity == right.FigureIdentity && left.DocumentIdentity == right.DocumentIdentity && left.MaterialId.Equals(right.MaterialId) && left.SourceMaterialIdentity == right.SourceMaterialIdentity && left.Semantic == right.Semantic && left.Layer == right.Layer && left.Participation == right.Participation && left.PageIndex == right.PageIndex && left.X == right.X && left.Y == right.Y && left.Width == right.Width && left.Height == right.Height && left.Gutter == right.Gutter && left.ComparisonMode == right.ComparisonMode;
        private static bool Fail(string code, out StackMachineDiagnostic diagnostic) { diagnostic = StackMachineDiagnostic.CreateDomain("atlas", code, "Atlas Cross Oracle rejected its evidence context."); return false; }
    }
}
