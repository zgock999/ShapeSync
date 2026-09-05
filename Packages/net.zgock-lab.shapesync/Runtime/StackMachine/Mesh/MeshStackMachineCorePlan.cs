// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>Identifies the detached semantic of one Mesh recipe binding.</summary>
    public enum MeshCoreBindingKind { Morph, Outfit, Normal }

    /// <summary>Describes one detached logical Mesh binding without a runtime Figure component reference.</summary>
    public readonly struct MeshCoreBinding
    {
        /// <summary>Creates a logical FBM binding.</summary>
        public static MeshCoreBinding Morph(string logicalName, string targetName) => new MeshCoreBinding(logicalName, targetName, null, MeshCoreBindingKind.Morph, false, false);
        /// <summary>Creates a logical Outfit binding.</summary>
        public static MeshCoreBinding Outfit(string logicalName, string registryId) => Outfit(logicalName, registryId, false, false);
        /// <summary>Creates a logical Outfit binding and declares its detached PCM / BCP source roles.</summary>
        public static MeshCoreBinding Outfit(string logicalName, string registryId, bool hasPcmSource, bool hasBcpSource) => new MeshCoreBinding(logicalName, null, registryId, MeshCoreBindingKind.Outfit, hasPcmSource, hasBcpSource);
        /// <summary>Creates a logical NormalBlender entry binding.</summary>
        public static MeshCoreBinding Normal(string logicalName) => new MeshCoreBinding(logicalName, null, null, MeshCoreBindingKind.Normal, false, false);

        private MeshCoreBinding(string logicalName, string targetName, string registryId, MeshCoreBindingKind kind, bool hasPcmSource, bool hasBcpSource)
        {
            LogicalName = logicalName;
            TargetName = targetName;
            RegistryId = registryId;
            Kind = kind;
            HasPcmSource = hasPcmSource;
            HasBcpSource = hasBcpSource;
        }

        /// <summary>Gets the document logical binding name.</summary>
        public string LogicalName { get; }
        /// <summary>Gets the DynamicBoneBlendTarget name for a morph binding.</summary>
        public string TargetName { get; }
        /// <summary>Gets the Outfit registry identifier for an Outfit binding.</summary>
        public string RegistryId { get; }
        /// <summary>Gets the detached binding semantic.</summary>
        public MeshCoreBindingKind Kind { get; }
        /// <summary>Gets whether this Outfit contributes a PCM application source after ATTACH.</summary>
        public bool HasPcmSource { get; }
        /// <summary>Gets whether this Outfit contributes a BCP application source after ATTACH.</summary>
        public bool HasBcpSource { get; }
    }

    /// <summary>Identifies one execution-free logical Mesh operation.</summary>
    public enum MeshCoreOperationKind
    {
        /// <summary>Logical representation of <c>MORPH_RESET</c>.</summary>
        MorphReset,
        /// <summary>Logical representation of <c>DETACH</c>.</summary>
        Detach,
        /// <summary>Logical representation of <c>DETACH_ALL</c>.</summary>
        DetachAll,
        /// <summary>Sets one logical FBM weight.</summary>
        SetMorph,
        /// <summary>Registers one Outfit as a transformed build candidate.</summary>
        AttachOutfit
    }

    /// <summary>One immutable logical Mesh operation lowered from a Mesh recipe.</summary>
    public readonly struct MeshCoreOperation
    {
        internal MeshCoreOperation(MeshCoreOperationKind kind, int instructionPointer, string logicalName, string targetName, string registryId, float weight, bool registersPcmSource = false, bool registersBcpSource = false)
        {
            Kind = kind;
            InstructionPointer = instructionPointer;
            LogicalName = logicalName;
            TargetName = targetName;
            RegistryId = registryId;
            Weight = weight;
            RegistersPcmSource = registersPcmSource;
            RegistersBcpSource = registersBcpSource;
        }

        /// <summary>Gets the logical operation kind.</summary>
        public MeshCoreOperationKind Kind { get; }
        /// <summary>Gets the common-plan instruction pointer that produced this operation.</summary>
        public int InstructionPointer { get; }
        /// <summary>Gets the source logical binding when applicable.</summary>
        public string LogicalName { get; }
        /// <summary>Gets the morph target name for <see cref="MeshCoreOperationKind.SetMorph"/>.</summary>
        public string TargetName { get; }
        /// <summary>Gets the Outfit registry identifier for <see cref="MeshCoreOperationKind.AttachOutfit"/>.</summary>
        public string RegistryId { get; }
        /// <summary>Gets the FBM weight for <see cref="MeshCoreOperationKind.SetMorph"/>.</summary>
        public float Weight { get; }
        /// <summary>Gets whether an ATTACH operation registers this Outfit as a PCM source.</summary>
        public bool RegistersPcmSource { get; }
        /// <summary>Gets whether an ATTACH operation registers this Outfit as a BCP source.</summary>
        public bool RegistersBcpSource { get; }
    }

    /// <summary>
    /// Immutable, execution-free Mesh recipe artifact shared by runtime and EditMode backends.
    /// </summary>
    /// <remarks>
    /// This Core plan owns only detached binding metadata, common bytecode, and initial-state logical lower results.
    /// It never accesses DynamicBoneBlender, OutfitAttacher, GameObject, Mesh, Avatar, Normal completion, transaction,
    /// cancellation, or disposal state. Concrete backends own those physical concerns.
    /// </remarks>
    public sealed class MeshStackMachineCorePlan
    {
        private readonly MeshCoreOperation[] operations;

        private readonly NormalRecipeTemplate[] normalTemplates;

        private MeshStackMachineCorePlan(StackMachinePlan commonPlan, MeshCoreOperation[] operations, NormalRecipeTemplate[] normalTemplates)
        {
            CommonPlan = commonPlan;
            this.operations = operations;
            this.normalTemplates = normalTemplates;
        }

        /// <summary>Gets the immutable common StackMachine bytecode.</summary>
        public StackMachinePlan CommonPlan { get; }
        /// <summary>Gets the initial-state logical operations in recipe order.</summary>
        public IReadOnlyList<MeshCoreOperation> Operations => Array.AsReadOnly(operations);
        /// <summary>Gets detached Mesh-owned Normal templates in declaration order.</summary>
        public IReadOnlyList<NormalRecipeTemplate> NormalTemplates => Array.AsReadOnly(normalTemplates);

        /// <summary>Compiles and lowers a detached Mesh recipe without starting physical execution.</summary>
        public static bool TryCreate(MeshRecipeDocument document, IReadOnlyList<MeshCoreBinding> bindings, out MeshStackMachineCorePlan plan, out StackMachineDiagnostic diagnostic)
        {
            plan = null;
            if (document == null) return Fail("DocumentRequired", "Mesh Core plan requires a MeshRecipeDocument.", out diagnostic);
            if (!TryCreateBindings(bindings, out Dictionary<string, MeshCoreBinding> byName, out diagnostic)) return false;
            if (!MeshNormalBlockParser.TryExtract(document.wordSource, out string meshWordSource, out IReadOnlyList<NormalRecipeTemplate> normalTemplates, out diagnostic)) return false;
            if (!ValidateNormalBindings(byName, normalTemplates, out diagnostic)) return false;
            MeshRecipeDocument outerDocument = CreateOuterDocument(document, meshWordSource, byName);
            StackMachinePlan commonPlan;
            if (string.IsNullOrWhiteSpace(meshWordSource)) commonPlan = new StackMachinePlan(Array.Empty<StackMachineInstruction>());
            else if (!TryCompile(outerDocument, out commonPlan, out diagnostic)) return false;
            if (!TryLower(commonPlan, byName, out MeshCoreOperation[] operations, out diagnostic)) return false;

            plan = new MeshStackMachineCorePlan(commonPlan, operations, CopyTemplates(normalTemplates));
            return true;
        }

        /// <summary>Compiles one Mesh recipe through the shared Mesh WordSet without physical execution.</summary>
        internal static bool TryCompile(MeshRecipeDocument document, out StackMachinePlan commonPlan, out StackMachineDiagnostic diagnostic)
        {
            commonPlan = null;
            diagnostic = null;
            if (document == null) return Fail("DocumentRequired", "Mesh Core plan requires a MeshRecipeDocument.", out diagnostic);
            var registry = new StackMachineWordRegistry();
            new MeshWordSet().RegisterInto(registry);
            return StackMachineCompiler.TryCompile(document, registry, out commonPlan, out diagnostic);
        }

        /// <summary>Creates the runtime Core carrier without performing Compiler-only logical lower validation.</summary>
        /// <remarks>Runtime backends keep their established binding and semantic diagnostic ownership.</remarks>
        internal static bool TryCreateRuntime(MeshRecipeDocument document, out MeshStackMachineCorePlan plan, out StackMachineDiagnostic diagnostic)
        {
            plan = null;
            if (document == null) return Fail("DocumentRequired", "Mesh Core plan requires a MeshRecipeDocument.", out diagnostic);
            if (!MeshNormalBlockParser.TryExtract(document.wordSource, out string meshWordSource, out IReadOnlyList<NormalRecipeTemplate> normalTemplates, out diagnostic)) return false;
            StackMachinePlan commonPlan;
            if (string.IsNullOrWhiteSpace(meshWordSource)) commonPlan = new StackMachinePlan(Array.Empty<StackMachineInstruction>());
            else if (!TryCompile(CreateOuterDocument(document, meshWordSource), out commonPlan, out diagnostic)) return false;
            plan = new MeshStackMachineCorePlan(commonPlan, Array.Empty<MeshCoreOperation>(), CopyTemplates(normalTemplates));
            return true;
        }

        private static bool TryCreateBindings(IReadOnlyList<MeshCoreBinding> bindings, out Dictionary<string, MeshCoreBinding> byName, out StackMachineDiagnostic diagnostic)
        {
            byName = new Dictionary<string, MeshCoreBinding>(StringComparer.Ordinal);
            diagnostic = null;
            if (bindings == null) return Fail("BindingRequired", "Mesh Core plan requires detached bindings.", out diagnostic);
            for (int i = 0; i < bindings.Count; i++)
            {
                MeshCoreBinding binding = bindings[i];
                if (string.IsNullOrEmpty(binding.LogicalName)) return Fail("BindingInvalid", "Mesh Core binding logical name is required.", out diagnostic);
                if (binding.Kind != MeshCoreBindingKind.Morph && binding.Kind != MeshCoreBindingKind.Outfit && binding.Kind != MeshCoreBindingKind.Normal)
                    return Fail("BindingInvalid", "Mesh Core binding kind is unsupported.", out diagnostic, binding: binding.LogicalName);
                if (binding.Kind == MeshCoreBindingKind.Morph && string.IsNullOrEmpty(binding.TargetName)) return Fail("BindingInvalid", "Morph binding target name is required.", out diagnostic, binding: binding.LogicalName);
                if (binding.Kind == MeshCoreBindingKind.Outfit && string.IsNullOrEmpty(binding.RegistryId)) return Fail("BindingInvalid", "Outfit binding registry identifier is required.", out diagnostic, binding: binding.LogicalName);
                if (!byName.TryAdd(binding.LogicalName, binding)) return Fail("DuplicateBinding", "Mesh Core binding logical names must be unique.", out diagnostic, binding: binding.LogicalName);
            }
            return true;
        }

        private static MeshRecipeDocument CreateOuterDocument(MeshRecipeDocument document, string meshWordSource)
            => new MeshRecipeDocument { recipeFormatVersion = document.recipeFormatVersion, wordSource = meshWordSource, bindings = document.bindings, capabilities = document.capabilities, provenance = document.provenance, diagnosticSourceMap = document.diagnosticSourceMap };

        private static MeshRecipeDocument CreateOuterDocument(MeshRecipeDocument document, string meshWordSource, IReadOnlyDictionary<string, MeshCoreBinding> bindings)
            => new MeshRecipeDocument
            {
                recipeFormatVersion = document.recipeFormatVersion,
                wordSource = meshWordSource,
                bindings = CreateCompileDeclarations(bindings),
                capabilities = document.capabilities,
                provenance = document.provenance,
                diagnosticSourceMap = document.diagnosticSourceMap
            };

        private static List<StackMachineBindingDeclaration> CreateCompileDeclarations(IReadOnlyDictionary<string, MeshCoreBinding> bindings)
        {
            var declarations = new List<StackMachineBindingDeclaration>(bindings.Count);
            foreach (KeyValuePair<string, MeshCoreBinding> pair in bindings)
                declarations.Add(new StackMachineBindingDeclaration { logicalName = pair.Key, declaredKind = StackMachineBindingKind.Resource });
            return declarations;
        }

        private static NormalRecipeTemplate[] CopyTemplates(IReadOnlyList<NormalRecipeTemplate> templates)
        {
            var copy = new NormalRecipeTemplate[templates.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = templates[i];
            return copy;
        }

        private static bool ValidateNormalBindings(IReadOnlyDictionary<string, MeshCoreBinding> bindings, IReadOnlyList<NormalRecipeTemplate> normalTemplates, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            for (int i = 0; i < normalTemplates.Count; i++)
            {
                NormalRecipeTemplate template = normalTemplates[i];
                if (!bindings.TryGetValue(template.EntryName, out MeshCoreBinding binding) || binding.Kind != MeshCoreBindingKind.Normal)
                    return Fail("NormalBindingRequired", "NORMAL requires one detached Normal entry binding.", out diagnostic, binding: template.EntryName);
            }
            return true;
        }

        private static bool TryLower(StackMachinePlan commonPlan, IReadOnlyDictionary<string, MeshCoreBinding> bindings, out MeshCoreOperation[] operations, out StackMachineDiagnostic diagnostic)
        {
            operations = null;
            var stack = new List<Value>();
            var lowered = new List<MeshCoreOperation>();
            var seenMorphTargets = new HashSet<string>(StringComparer.Ordinal);
            var seenAttachRegistryIds = new HashSet<string>(StringComparer.Ordinal);
            diagnostic = null;
            for (int ip = 0; ip < commonPlan.Instructions.Count; ip++)
            {
                StackMachineInstruction instruction = commonPlan.Instructions[ip];
                if (instruction.Op == StackMachineOp.PushNumber) { stack.Add(Value.FromNumber(instruction.Number)); continue; }
                if (instruction.Op == StackMachineOp.PushBoolean) { stack.Add(Value.FromBoolean(instruction.Boolean)); continue; }
                if (instruction.Op == StackMachineOp.PushBinding)
                {
                    if (!bindings.TryGetValue(instruction.Binding, out MeshCoreBinding binding)) return Fail("BindingMissing", "Compiled Mesh recipe refers to an unresolved detached binding.", out diagnostic, ip, binding: instruction.Binding);
                    stack.Add(Value.FromBinding(binding));
                    continue;
                }
                if (instruction.Op != StackMachineOp.DomainWord)
                {
                    if (!TryExecuteBuiltIn(instruction.Op, stack, ip, out diagnostic)) { operations = null; return false; }
                    continue;
                }
                if (instruction.WordId == MeshWordSet.MorphReset) { lowered.Add(new MeshCoreOperation(MeshCoreOperationKind.MorphReset, ip, null, null, null, 0f)); continue; }
                if (instruction.WordId == MeshWordSet.DetachAll) { lowered.Add(new MeshCoreOperation(MeshCoreOperationKind.DetachAll, ip, null, null, null, 0f)); continue; }
                if (instruction.WordId == MeshWordSet.FbmSet)
                {
                    if (!Pop(stack, ValueTag.Number, ip, out Value number, out diagnostic) || !Pop(stack, ValueTag.Binding, ip, out Value morphValue, out diagnostic)) { operations = null; return false; }
                    MeshCoreBinding morph = morphValue.Binding;
                    if (morph.Kind != MeshCoreBindingKind.Morph) return Fail("MorphBindingRequired", "FBM_SET requires a morph binding.", out diagnostic, ip, instruction.WordId, morph.LogicalName);
                    if (!IsFiniteFloat(number.Number)) return Fail("InvalidMorphWeight", "Morph weight must be finite float range.", out diagnostic, ip, instruction.WordId, morph.LogicalName, morph.TargetName);
                    if (!seenMorphTargets.Add(morph.TargetName)) return Fail("DuplicateMorph", "The recipe writes the same morph more than once.", out diagnostic, ip, instruction.WordId, morph.LogicalName, morph.TargetName);
                    lowered.Add(new MeshCoreOperation(MeshCoreOperationKind.SetMorph, ip, morph.LogicalName, morph.TargetName, null, (float)number.Number));
                    continue;
                }
                if (instruction.WordId == MeshWordSet.OutfitAttach || instruction.WordId == MeshWordSet.OutfitDetach)
                {
                    if (!Pop(stack, ValueTag.Binding, ip, out Value outfitValue, out diagnostic)) { operations = null; return false; }
                    MeshCoreBinding outfit = outfitValue.Binding;
                    if (outfit.Kind != MeshCoreBindingKind.Outfit) return Fail("OutfitBindingRequired", "Outfit word requires an Outfit binding.", out diagnostic, ip, instruction.WordId, outfit.LogicalName);
                    if (instruction.WordId == MeshWordSet.OutfitDetach)
                    {
                        lowered.Add(new MeshCoreOperation(MeshCoreOperationKind.Detach, ip, outfit.LogicalName, null, outfit.RegistryId, 0f));
                        continue;
                    }
                    if (!seenAttachRegistryIds.Add(outfit.RegistryId)) return Fail("DuplicateRegistryId", "The recipe attaches the same Outfit registry more than once.", out diagnostic, ip, instruction.WordId, outfit.LogicalName, outfit.RegistryId);
                    lowered.Add(new MeshCoreOperation(MeshCoreOperationKind.AttachOutfit, ip, outfit.LogicalName, null, outfit.RegistryId, 0f, outfit.HasPcmSource, outfit.HasBcpSource));
                    continue;
                }
                operations = null;
                return Fail("UnsupportedMeshWord", "Compiled Mesh recipe contains an unsupported Mesh word.", out diagnostic, ip, instruction.WordId);
            }
            operations = lowered.ToArray();
            return true;
        }

        private enum ValueTag { Number, Boolean, Binding }

        private readonly struct Value
        {
            private Value(ValueTag tag, double number, bool boolean, MeshCoreBinding binding) { Tag = tag; Number = number; Boolean = boolean; Binding = binding; }
            public ValueTag Tag { get; }
            public double Number { get; }
            public bool Boolean { get; }
            public MeshCoreBinding Binding { get; }
            public static Value FromNumber(double value) => new Value(ValueTag.Number, value, false, default);
            public static Value FromBoolean(bool value) => new Value(ValueTag.Boolean, 0d, value, default);
            public static Value FromBinding(MeshCoreBinding value) => new Value(ValueTag.Binding, 0d, false, value);
        }

        private static bool TryExecuteBuiltIn(StackMachineOp op, List<Value> stack, int ip, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (op == StackMachineOp.Depth) { stack.Add(Value.FromNumber(stack.Count)); return true; }
            if (op == StackMachineOp.Drop) { if (!Need(stack, 1, ip, out diagnostic)) return false; stack.RemoveAt(stack.Count - 1); return true; }
            if (op == StackMachineOp.Dup) { if (!Need(stack, 1, ip, out diagnostic)) return false; stack.Add(stack[stack.Count - 1]); return true; }
            if (op == StackMachineOp.Swap) { if (!Need(stack, 2, ip, out diagnostic)) return false; int n = stack.Count - 1; Value value = stack[n]; stack[n] = stack[n - 1]; stack[n - 1] = value; return true; }
            if (op == StackMachineOp.Over) { if (!Need(stack, 2, ip, out diagnostic)) return false; stack.Add(stack[stack.Count - 2]); return true; }
            if (op == StackMachineOp.Rot) { if (!Need(stack, 3, ip, out diagnostic)) return false; int n = stack.Count - 3; Value value = stack[n]; stack[n] = stack[n + 1]; stack[n + 1] = stack[n + 2]; stack[n + 2] = value; return true; }
            if (op == StackMachineOp.Call || op == StackMachineOp.Exit) return Fail("UnsupportedControlFlow", "Mesh Core plans do not support control-flow instructions.", out diagnostic, ip);
            if (op == StackMachineOp.Not) { if (!Pop(stack, ValueTag.Boolean, ip, out Value value, out diagnostic)) return false; stack.Add(Value.FromBoolean(!value.Boolean)); return true; }
            if (op == StackMachineOp.And || op == StackMachineOp.Or || op == StackMachineOp.Xor)
            {
                if (!Pop(stack, ValueTag.Boolean, ip, out Value right, out diagnostic) || !Pop(stack, ValueTag.Boolean, ip, out Value left, out diagnostic)) return false;
                stack.Add(Value.FromBoolean(op == StackMachineOp.And ? left.Boolean && right.Boolean : op == StackMachineOp.Or ? left.Boolean || right.Boolean : left.Boolean ^ right.Boolean));
                return true;
            }
            if (op == StackMachineOp.Equal)
            {
                if (!Need(stack, 2, ip, out diagnostic)) return false;
                int n = stack.Count - 1; Value right = stack[n], left = stack[n - 1];
                if (left.Tag != right.Tag || (left.Tag != ValueTag.Number && left.Tag != ValueTag.Boolean)) { diagnostic = StackMachineDiagnostic.Create(StackMachineDiagnosticCode.TypeMismatch, "= requires two Numbers or two Booleans.", ip); return false; }
                stack.RemoveRange(n - 1, 2); stack.Add(Value.FromBoolean(left.Tag == ValueTag.Number ? left.Number == right.Number : left.Boolean == right.Boolean)); return true;
            }
            if (op != StackMachineOp.Add && op != StackMachineOp.Subtract && op != StackMachineOp.Multiply && op != StackMachineOp.Divide && op != StackMachineOp.Min && op != StackMachineOp.Max && op != StackMachineOp.Negate && op != StackMachineOp.Abs && op != StackMachineOp.Increment && op != StackMachineOp.Decrement && op != StackMachineOp.Double && op != StackMachineOp.Halve && op != StackMachineOp.ZeroEqual && op != StackMachineOp.ZeroLess && op != StackMachineOp.Less && op != StackMachineOp.Greater)
                return Fail("UnsupportedBuiltIn", "Mesh Core plan contains an unsupported built-in operation.", out diagnostic, ip);
            if (!Pop(stack, ValueTag.Number, ip, out Value rightNumberValue, out diagnostic)) return false;
            if (op == StackMachineOp.Negate || op == StackMachineOp.Abs || op == StackMachineOp.Increment || op == StackMachineOp.Decrement || op == StackMachineOp.Double || op == StackMachineOp.Halve || op == StackMachineOp.ZeroEqual || op == StackMachineOp.ZeroLess)
            {
                double value = rightNumberValue.Number;
                if (op == StackMachineOp.ZeroEqual || op == StackMachineOp.ZeroLess) { stack.Add(Value.FromBoolean(op == StackMachineOp.ZeroEqual ? value == 0d : value < 0d)); return true; }
                double result = op == StackMachineOp.Negate ? -value : op == StackMachineOp.Abs ? Math.Abs(value) : op == StackMachineOp.Increment ? value + 1d : op == StackMachineOp.Decrement ? value - 1d : op == StackMachineOp.Double ? value * 2d : value / 2d;
                return PushFinite(stack, result, ip, out diagnostic);
            }
            if (!Pop(stack, ValueTag.Number, ip, out Value leftNumberValue, out diagnostic)) return false;
            double leftNumber = leftNumberValue.Number, rightNumber = rightNumberValue.Number;
            if (op == StackMachineOp.Divide && rightNumber == 0d) { diagnostic = StackMachineDiagnostic.Create(StackMachineDiagnosticCode.DivideByZero, "Division by zero.", ip); return false; }
            if (op == StackMachineOp.Less || op == StackMachineOp.Greater) { stack.Add(Value.FromBoolean(op == StackMachineOp.Less ? leftNumber < rightNumber : leftNumber > rightNumber)); return true; }
            double numeric = op == StackMachineOp.Add ? leftNumber + rightNumber : op == StackMachineOp.Subtract ? leftNumber - rightNumber : op == StackMachineOp.Multiply ? leftNumber * rightNumber : op == StackMachineOp.Divide ? leftNumber / rightNumber : op == StackMachineOp.Min ? Math.Min(leftNumber, rightNumber) : op == StackMachineOp.Max ? Math.Max(leftNumber, rightNumber) : double.NaN;
            if (double.IsNaN(numeric)) return Fail("UnsupportedBuiltIn", "Mesh Core plan contains an unsupported binary built-in operation.", out diagnostic, ip);
            return PushFinite(stack, numeric, ip, out diagnostic);
        }

        private static bool Pop(List<Value> stack, ValueTag tag, int ip, out Value value, out StackMachineDiagnostic diagnostic)
        {
            value = default;
            if (!Need(stack, 1, ip, out diagnostic)) return false;
            int index = stack.Count - 1; value = stack[index];
            if (value.Tag != tag) { diagnostic = StackMachineDiagnostic.Create(StackMachineDiagnosticCode.TypeMismatch, tag + " required.", ip); return false; }
            stack.RemoveAt(index);
            return true;
        }

        private static bool Need(IReadOnlyList<Value> stack, int count, int ip, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (stack.Count >= count) return true;
            diagnostic = StackMachineDiagnostic.Create(StackMachineDiagnosticCode.StackUnderflow, "Data stack underflow.", ip);
            return false;
        }

        private static bool PushFinite(List<Value> stack, double number, int ip, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (double.IsNaN(number) || double.IsInfinity(number)) { diagnostic = StackMachineDiagnostic.Create(StackMachineDiagnosticCode.NonFiniteNumber, "Non-finite numeric result.", ip); return false; }
            stack.Add(Value.FromNumber(number));
            return true;
        }

        private static bool IsFiniteFloat(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value >= float.MinValue && value <= float.MaxValue;

        private static bool Fail(string code, string message, out StackMachineDiagnostic diagnostic, int instructionPointer = -1, string wordId = null, string binding = null, string detail = null)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("mesh", code, message, instructionPointer: instructionPointer, wordId: wordId, bindingName: binding, detail: detail);
            return false;
        }
    }
}
