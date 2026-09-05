// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>Identifies the opaque payload interpretation of a data-stack value.</summary>
    public enum StackMachineValueTag { Empty, Number, Boolean, ResourceHandle, DomainValue }
    /// <summary>Classifies a compile, validation, or execution diagnostic.</summary>
    public enum StackMachineDiagnosticCode { None, EmptySource, UnknownToken, StackUnderflow, TypeMismatch, DivideByZero, NonFiniteNumber, InvalidBinding, UnsupportedRecipeVersion, MalformedDocument, InvalidControlFlow, DomainFailure }
    /// <summary>Describes the common lifecycle state of a domain transaction.</summary>
    public enum StackMachineTransactionStage { Started, Succeeded, Failed, RollbackCompleted }
    /// <summary>Identifies the execution strategy selected by a domain executor.</summary>
    public enum StackMachineDomainExecutionMode { Sequential, BytecodeTransaction, FullyCompiled }
    /// <summary>Declares whether a logical recipe binding resolves to a resource or domain-owned value.</summary>
    public enum StackMachineBindingKind { Resource, DomainValue }
    /// <summary>Identifies an immutable common-plan instruction.</summary>
    public enum StackMachineOp { PushNumber, PushBoolean, PushBinding, Depth, Drop, Dup, Swap, Over, Rot, Add, Subtract, Multiply, Divide, Negate, Abs, Min, Max, Increment, Decrement, Double, Halve, Equal, Less, Greater, ZeroEqual, ZeroLess, And, Or, Xor, Not, DomainWord, Call, Exit }

    /// <summary>Tagged scalar or opaque handle stored on the StackMachine data stack.</summary>
    [Serializable]
    public struct StackMachineValue
    {
        [SerializeField] private StackMachineValueTag tag;
        [SerializeField] private double number;
        [SerializeField] private bool boolean;
        [SerializeField] private int handle;
        /// <summary>Gets the tag that determines which payload property is meaningful.</summary>
        public StackMachineValueTag Tag => tag;
        /// <summary>Gets the numeric payload when <see cref="Tag"/> is <see cref="StackMachineValueTag.Number"/>.</summary>
        public double Number => number;
        /// <summary>Gets the Boolean payload when <see cref="Tag"/> is <see cref="StackMachineValueTag.Boolean"/>.</summary>
        public bool Boolean => boolean;
        /// <summary>Gets the opaque handle when <see cref="Tag"/> is <see cref="StackMachineValueTag.ResourceHandle"/> or <see cref="StackMachineValueTag.DomainValue"/>.</summary>
        public int Handle => handle;
        /// <summary>Creates a value tagged as <see cref="StackMachineValueTag.Number"/>.</summary>
        /// <param name="value">Numeric payload.</param>
        public static StackMachineValue FromNumber(double value) => new StackMachineValue { tag = StackMachineValueTag.Number, number = value };
        /// <summary>Creates a value tagged as <see cref="StackMachineValueTag.Boolean"/>.</summary>
        /// <param name="value">Boolean payload.</param>
        public static StackMachineValue FromBoolean(bool value) => new StackMachineValue { tag = StackMachineValueTag.Boolean, boolean = value };
        /// <summary>Creates an opaque resource handle without resolving it to a concrete Unity type.</summary>
        /// <param name="value">Domain-owned resource handle.</param>
        public static StackMachineValue FromResourceHandle(int value) => new StackMachineValue { tag = StackMachineValueTag.ResourceHandle, handle = value };
        /// <summary>Creates an opaque domain-value handle without resolving it to a concrete Unity type.</summary>
        /// <param name="value">Domain-owned value handle.</param>
        public static StackMachineValue FromDomainValue(int value) => new StackMachineValue { tag = StackMachineValueTag.DomainValue, handle = value };
        public override string ToString() => tag == StackMachineValueTag.Number ? number.ToString("G", CultureInfo.InvariantCulture) : tag == StackMachineValueTag.Boolean ? boolean.ToString() : tag == StackMachineValueTag.ResourceHandle ? "$" + handle : tag == StackMachineValueTag.DomainValue ? "#" + handle : tag.ToString();
    }

    /// <summary>Structured failure information shared by the common foundation and domain executors.</summary>
    [Serializable]
    public sealed class StackMachineDiagnostic
    {
        /// <summary>Common diagnostic classification.</summary>
        public StackMachineDiagnosticCode code;
        /// <summary>Human-readable failure detail.</summary>
        public string message;
        /// <summary>Source token index, or <c>-1</c> when unavailable.</summary>
        public int tokenIndex = -1;
        /// <summary>Instruction pointer, or <c>-1</c> when unavailable.</summary>
        public int instructionPointer = -1;
        /// <summary>Owning domain identifier for <see cref="StackMachineDiagnosticCode.DomainFailure"/>.</summary>
        public string domain;
        /// <summary>Stable domain-specific error code.</summary>
        public string domainCode;
        /// <summary>Stable domain word identifier involved in the failure.</summary>
        public string wordId;
        /// <summary>Logical binding involved in the failure.</summary>
        public string bindingName;
        /// <summary>Optional domain-specific failure detail.</summary>
        public string detail;
        /// <summary>Creates a common diagnostic.</summary>
        /// <param name="c">Diagnostic classification.</param><param name="m">Human-readable detail.</param><param name="i">Source token index, or <c>-1</c> when unavailable.</param>
        public static StackMachineDiagnostic Create(StackMachineDiagnosticCode c, string m, int i = -1) => new StackMachineDiagnostic { code = c, message = m, tokenIndex = i };
        /// <summary>Creates a domain diagnostic while preserving common result formatting.</summary>
        /// <param name="domain">Owning domain identifier.</param><param name="domainCode">Stable domain-specific error code.</param><param name="message">Human-readable detail.</param><param name="tokenIndex">Source token index, or <c>-1</c>.</param><param name="instructionPointer">Instruction index, or <c>-1</c>.</param><param name="wordId">Stable domain word identifier.</param><param name="bindingName">Logical binding involved in the failure.</param><param name="detail">Optional domain-specific detail.</param>
        public static StackMachineDiagnostic CreateDomain(string domain, string domainCode, string message, int tokenIndex = -1, int instructionPointer = -1, string wordId = null, string bindingName = null, string detail = null)
            => new StackMachineDiagnostic { code = StackMachineDiagnosticCode.DomainFailure, domain = domain, domainCode = domainCode, message = message, tokenIndex = tokenIndex, instructionPointer = instructionPointer, wordId = wordId, bindingName = bindingName, detail = detail };

        /// <summary>Formats a diagnostic without discarding structured context.</summary>
        /// <param name="diagnostic">Diagnostic to format, or <c>null</c>.</param>
        /// <param name="nullMessage">Fallback text used when <paramref name="diagnostic"/> is <c>null</c>.</param>
        public static string Format(StackMachineDiagnostic diagnostic, string nullMessage)
        {
            if (diagnostic == null)
                return string.IsNullOrEmpty(nullMessage) ? "Stack machine failed without a diagnostic." : nullMessage;

            string displayCode = string.IsNullOrEmpty(diagnostic.domainCode)
                ? diagnostic.code.ToString()
                : diagnostic.domainCode;
            var builder = new StringBuilder();
            builder.Append(displayCode).Append(": ").Append(diagnostic.message ?? string.Empty);
            builder.Append("\ncode=").Append(diagnostic.code.ToString());
            builder.Append("; domain=").Append(FieldOrNone(diagnostic.domain));
            builder.Append("; domainCode=").Append(FieldOrNone(diagnostic.domainCode));
            builder.Append("; tokenIndex=").Append(diagnostic.tokenIndex.ToString(CultureInfo.InvariantCulture));
            builder.Append("; instructionPointer=").Append(diagnostic.instructionPointer.ToString(CultureInfo.InvariantCulture));
            builder.Append("; wordId=").Append(FieldOrNone(diagnostic.wordId));
            builder.Append("; bindingName=").Append(FieldOrNone(diagnostic.bindingName));
            builder.Append("; detail=").Append(FieldOrNone(diagnostic.detail));
            return builder.ToString();
        }

        /// <summary>Returns the same structured, human-readable representation as <see cref="Format"/>.</summary>
        public override string ToString() => Format(this, "Stack machine failed without a diagnostic.");

        private static string FieldOrNone(string value) => string.IsNullOrEmpty(value) ? "<none>" : value;
    }
    /// <summary>Serializable provenance metadata for a recipe document.</summary>
    [Serializable] public sealed class StackMachineProvenance
    {
        /// <summary>Recipe author or producing system identifier.</summary>
        public string author;
        /// <summary>Optional free-form provenance note.</summary>
        public string note;
    }
    /// <summary>Maps a recipe token index to its source offset for domain diagnostics.</summary>
    [Serializable] public sealed class StackMachineSourceMapEntry
    {
        /// <summary>Zero-based token index in the canonical word source.</summary>
        public int tokenIndex;
        /// <summary>Zero-based character offset in the canonical word source.</summary>
        public int sourceOffset;
    }
    /// <summary>Serializable declaration of one logical recipe binding.</summary>
    [Serializable] public sealed class StackMachineBindingDeclaration
    {
        /// <summary>Abstract binding name referenced by <c>$logicalName</c> source tokens.</summary>
        public string logicalName;
        /// <summary>Declared opaque value category used for domain validation.</summary>
        public StackMachineBindingKind declaredKind;
    }
    /// <summary>Serializable, Unity-object-free representation of common recipe content.</summary>
    [Serializable] public class StackMachineRecipeDocument
    {
        /// <summary>Serialized common recipe format version. Spec12 supports version <c>1</c>.</summary>
        public int recipeFormatVersion = 1;
        /// <summary>Whitespace-tokenized Forth subset source that contains no concrete Unity references.</summary>
        [TextArea(3, 16)] public string wordSource;
        /// <summary>Serializable logical binding declarations.</summary>
        [HideInInspector] public List<StackMachineBindingDeclaration> bindings = new List<StackMachineBindingDeclaration>();
        /// <summary>Domain capability names required to interpret this recipe.</summary>
        [HideInInspector] public List<string> capabilities = new List<string>();
        /// <summary>Authoring provenance metadata.</summary>
        [HideInInspector] public StackMachineProvenance provenance = new StackMachineProvenance();
        /// <summary>Optional token-to-source offsets for structured diagnostics.</summary>
        [HideInInspector] public List<StackMachineSourceMapEntry> diagnosticSourceMap = new List<StackMachineSourceMapEntry>();
        /// <summary>Validates fields owned by a derived domain recipe.</summary>
        /// <remarks>Override this method for domain metadata only. Common format, binding, and JSON validation remains owned by <see cref="StackMachineRecipeSerialization.TryValidateDocument"/>.</remarks>
        /// <param name="diagnostic">Receives a structured failure diagnostic.</param>
        /// <returns><see langword="true"/> when domain fields are valid.</returns>
        public virtual bool TryValidateDomain(out StackMachineDiagnostic diagnostic) { diagnostic = null; return true; }
    }

    /// <summary>ScriptableObject carrier for a recipe document and any domain-owned serialized fields.</summary>
    public abstract class StackMachineRecipeSerializationBase : ScriptableObject
    {
        /// <summary>Creates a detached document copy from this serialized carrier.</summary>
        /// <remarks>Derived carriers must deep-copy both common and domain-owned fields. The returned document must not retain serialized Unity object references from the carrier.</remarks>
        /// <returns>A deep-copy-safe recipe document.</returns>
        public abstract StackMachineRecipeDocument ToDocument();
        /// <summary>Writes a detached recipe document into this carrier.</summary>
        /// <remarks>Derived carriers must deep-copy both common and domain-owned fields. Callers validate the document before this method is invoked.</remarks>
        /// <param name="document">Validated document to copy into the carrier.</param>
        public abstract void SetDocument(StackMachineRecipeDocument document);
    }

    /// <summary>Default ScriptableObject carrier for a common StackMachine recipe.</summary>
    public class StackMachineRecipeAsset : StackMachineRecipeSerializationBase
    {
        [SerializeField] private int recipeFormatVersion = 1;
        [SerializeField, TextArea] private string wordSource;
        [SerializeField] private List<StackMachineBindingDeclaration> bindings = new List<StackMachineBindingDeclaration>();
        [SerializeField] private List<string> capabilities = new List<string>();
        [SerializeField] private StackMachineProvenance provenance = new StackMachineProvenance();
        [SerializeField] private List<StackMachineSourceMapEntry> diagnosticSourceMap = new List<StackMachineSourceMapEntry>();
        /// <inheritdoc />
        public override StackMachineRecipeDocument ToDocument() => StackMachineRecipeClone.Copy(new StackMachineRecipeDocument { recipeFormatVersion = recipeFormatVersion, wordSource = wordSource, bindings = bindings, capabilities = capabilities, provenance = provenance, diagnosticSourceMap = diagnosticSourceMap });
        /// <inheritdoc />
        public override void SetDocument(StackMachineRecipeDocument d) { var copy = StackMachineRecipeClone.Copy(d); recipeFormatVersion = copy.recipeFormatVersion; wordSource = copy.wordSource; bindings = copy.bindings; capabilities = copy.capabilities; provenance = copy.provenance; diagnosticSourceMap = copy.diagnosticSourceMap; }
    }

    internal static class StackMachineRecipeClone
    {
        internal static StackMachineRecipeDocument Copy(StackMachineRecipeDocument source)
        {
            if (source == null) return null;
            var copy = new StackMachineRecipeDocument { recipeFormatVersion = source.recipeFormatVersion, wordSource = source.wordSource, provenance = source.provenance == null ? new StackMachineProvenance() : new StackMachineProvenance { author = source.provenance.author, note = source.provenance.note } };
            if (source.bindings != null) foreach (var binding in source.bindings) copy.bindings.Add(binding == null ? null : new StackMachineBindingDeclaration { logicalName = binding.logicalName, declaredKind = binding.declaredKind });
            if (source.capabilities != null) copy.capabilities.AddRange(source.capabilities);
            if (source.diagnosticSourceMap != null) foreach (var entry in source.diagnosticSourceMap) copy.diagnosticSourceMap.Add(entry == null ? null : new StackMachineSourceMapEntry { tokenIndex = entry.tokenIndex, sourceOffset = entry.sourceOffset });
            return copy;
        }
    }

    /// <summary>Non-serialized lookup constructed from a recipe binding template at initialization time.</summary>
    public sealed class StackMachineBindingLookup
    {
        private readonly Dictionary<string, StackMachineBindingDeclaration> byName;
        private StackMachineBindingLookup(Dictionary<string, StackMachineBindingDeclaration> entries) { byName = entries; }
        /// <summary>Looks up a declared binding by its logical name.</summary>
        /// <param name="logicalName">Recipe logical binding name.</param><param name="declaration">Receives the declaration when present.</param>
        /// <returns><see langword="true"/> when the name is declared.</returns>
        public bool TryGet(string logicalName, out StackMachineBindingDeclaration declaration) => byName.TryGetValue(logicalName, out declaration);
        /// <summary>Builds a non-serialized lookup from a validated recipe binding template.</summary>
        /// <param name="document">Recipe document that owns the binding declarations.</param><param name="lookup">Receives the lookup on success.</param><param name="diagnostic">Receives a validation failure.</param>
        /// <returns><see langword="true"/> when the lookup was built.</returns>
        public static bool TryCreate(StackMachineRecipeDocument document, out StackMachineBindingLookup lookup, out StackMachineDiagnostic diagnostic)
        {
            lookup = null;
            if (!StackMachineRecipeSerialization.TryValidateDocument(document, out diagnostic)) return false;
            var entries = new Dictionary<string, StackMachineBindingDeclaration>(StringComparer.Ordinal);
            if (document.bindings != null) foreach (var binding in document.bindings) entries.Add(binding.logicalName, new StackMachineBindingDeclaration { logicalName = binding.logicalName, declaredKind = binding.declaredKind });
            lookup = new StackMachineBindingLookup(entries); return true;
        }
    }

    /// <summary>Validates and serializes common recipe documents without retaining Unity object references.</summary>
    public static class StackMachineRecipeSerialization
    {
        /// <summary>Validates a document and serializes it as indented JSON.</summary>
        public static bool TrySerializeJson(StackMachineRecipeDocument document, out string json, out StackMachineDiagnostic diagnostic) { json = null; diagnostic = null; if (!TryValidateDocument(document, out diagnostic)) return false; json = JsonUtility.ToJson(document, true); return true; }
        /// <summary>Deserializes and validates a common recipe JSON document.</summary>
        public static bool TryDeserializeJson(string json, out StackMachineRecipeDocument document, out StackMachineDiagnostic diagnostic) { document = null; diagnostic = null; if (string.IsNullOrWhiteSpace(json)) { diagnostic = StackMachineDiagnostic.Create(StackMachineDiagnosticCode.MalformedDocument, "Recipe JSON is empty."); return false; } try { document = JsonUtility.FromJson<StackMachineRecipeDocument>(json); } catch (ArgumentException e) { diagnostic = StackMachineDiagnostic.Create(StackMachineDiagnosticCode.MalformedDocument, e.Message); return false; } return TryValidateDocument(document, out diagnostic); }
        /// <summary>Validates a derived document and serializes it as indented JSON.</summary>
        public static bool TrySerializeJson<TDocument>(TDocument document, out string json, out StackMachineDiagnostic diagnostic) where TDocument : StackMachineRecipeDocument { return TrySerializeJson((StackMachineRecipeDocument)document, out json, out diagnostic); }
        /// <summary>Deserializes and validates a derived recipe JSON document.</summary>
        public static bool TryDeserializeJson<TDocument>(string json, out TDocument document, out StackMachineDiagnostic diagnostic) where TDocument : StackMachineRecipeDocument { document = null; diagnostic = null; if (string.IsNullOrWhiteSpace(json)) { diagnostic = StackMachineDiagnostic.Create(StackMachineDiagnosticCode.MalformedDocument, "Recipe JSON is empty."); return false; } try { document = JsonUtility.FromJson<TDocument>(json); } catch (ArgumentException e) { diagnostic = StackMachineDiagnostic.Create(StackMachineDiagnosticCode.MalformedDocument, e.Message); return false; } return TryValidateDocument(document, out diagnostic); }
        /// <summary>Copies, then validates a document represented by a ScriptableObject carrier.</summary>
        public static bool TryDeserializeAsset(StackMachineRecipeSerializationBase asset, out StackMachineRecipeDocument document, out StackMachineDiagnostic diagnostic) { document = null; if (asset == null) { diagnostic = StackMachineDiagnostic.Create(StackMachineDiagnosticCode.MalformedDocument, "Recipe asset is null."); return false; } document = asset.ToDocument(); return TryValidateDocument(document, out diagnostic); }
        /// <summary>Validates a document before copying it to a ScriptableObject carrier.</summary>
        public static bool TryWriteAsset(StackMachineRecipeDocument document, StackMachineRecipeSerializationBase asset, out StackMachineDiagnostic diagnostic) { if (asset == null) { diagnostic = StackMachineDiagnostic.Create(StackMachineDiagnosticCode.MalformedDocument, "Recipe asset is null."); return false; } if (!TryValidateDocument(document, out diagnostic)) return false; asset.SetDocument(document); return true; }
        /// <summary>Validates common recipe format, binding uniqueness, and derived domain fields.</summary>
        public static bool TryValidateDocument(StackMachineRecipeDocument d, out StackMachineDiagnostic diagnostic) { diagnostic = null; if (d == null) { diagnostic = StackMachineDiagnostic.Create(StackMachineDiagnosticCode.MalformedDocument, "Recipe document is null."); return false; } if (d.recipeFormatVersion != 1) { diagnostic = StackMachineDiagnostic.Create(StackMachineDiagnosticCode.UnsupportedRecipeVersion, "Only recipe format version 1 is supported."); return false; } var names = new HashSet<string>(); if (d.bindings != null) foreach (var b in d.bindings) { if (b == null || string.IsNullOrWhiteSpace(b.logicalName) || !names.Add(b.logicalName)) { diagnostic = StackMachineDiagnostic.Create(StackMachineDiagnosticCode.InvalidBinding, "Bindings must have unique logical names."); return false; } } return d.TryValidateDomain(out diagnostic); }
    }

    /// <summary>Immutable declared data-stack effect for a domain word.</summary>
    public sealed class StackMachineWordSignature
    {
        private readonly IReadOnlyList<StackMachineValueTag> inputs;
        private readonly IReadOnlyList<StackMachineValueTag> outputs;
        /// <summary>Gets the required input tags in bottom-to-top order.</summary>
        public IReadOnlyList<StackMachineValueTag> Inputs => inputs;
        /// <summary>Gets the output tags pushed in bottom-to-top order.</summary>
        public IReadOnlyList<StackMachineValueTag> Outputs => outputs;
        /// <summary>Creates an immutable signature from copied tag arrays.</summary>
        /// <param name="inputTags">Required data-stack inputs, or <see langword="null"/> for none.</param><param name="outputTags">Produced data-stack outputs, or <see langword="null"/> for none.</param>
        public StackMachineWordSignature(StackMachineValueTag[] inputTags, StackMachineValueTag[] outputTags)
        {
            inputs = Array.AsReadOnly(inputTags == null ? Array.Empty<StackMachineValueTag>() : (StackMachineValueTag[])inputTags.Clone());
            outputs = Array.AsReadOnly(outputTags == null ? Array.Empty<StackMachineValueTag>() : (StackMachineValueTag[])outputTags.Clone());
        }
    }

    /// <summary>Immutable instruction in a validated common plan.</summary>
    public readonly struct StackMachineInstruction
    {
        /// <summary>Operation to perform.</summary>
        public readonly StackMachineOp Op;
        /// <summary>Numeric literal payload.</summary>
        public readonly double Number;
        /// <summary>Boolean literal payload.</summary>
        public readonly bool Boolean;
        /// <summary>Logical binding name.</summary>
        public readonly string Binding;
        /// <summary>Stable domain word identifier.</summary>
        public readonly string WordId;
        /// <summary>Control-flow target instruction index.</summary>
        public readonly int Target;
        /// <summary>Domain-word stack signature.</summary>
        public readonly StackMachineWordSignature Signature;
        /// <summary>Creates an immutable instruction with optional literal, binding, domain, and target data.</summary>
        public StackMachineInstruction(StackMachineOp op, double number = 0, bool boolean = false, string binding = null, string wordId = null, int target = -1, StackMachineWordSignature signature = null) { Op = op; Number = number; Boolean = boolean; Binding = binding; WordId = wordId; Target = target; Signature = signature; }
        /// <summary>Returns a copy whose control-flow target is <paramref name="target"/>.</summary>
        public StackMachineInstruction WithTarget(int target) => new StackMachineInstruction(Op, Number, Boolean, Binding, WordId, target, Signature);
    }

    /// <summary>Immutable common bytecode plan. Domain compilers may derive their own execution artifacts from it.</summary>
    public sealed class StackMachinePlan
    {
        private readonly IReadOnlyList<StackMachineInstruction> instructions;
        /// <summary>Gets the read-only instruction sequence.</summary>
        public IReadOnlyList<StackMachineInstruction> Instructions => instructions;
        /// <summary>Creates a plan by defensively copying the source array.</summary>
        /// <param name="source">Validated instruction sequence, or <see langword="null"/> for an empty plan.</param>
        public StackMachinePlan(StackMachineInstruction[] source)
        {
            var copy = source == null ? Array.Empty<StackMachineInstruction>() : (StackMachineInstruction[])source.Clone();
            instructions = Array.AsReadOnly(copy);
        }
    }

    /// <summary>
    /// Target-patching construction seam for a later user-word/control-flow compiler. Spec12 does not
    /// emit branch syntax, but later domains can patch instruction targets without replacing Plan.
    /// </summary>
    public sealed class StackMachinePlanBuilder
    {
        private readonly List<StackMachineInstruction> instructions = new List<StackMachineInstruction>();
        /// <summary>Gets the number of emitted instructions.</summary>
        public int Count => instructions.Count;
        /// <summary>Appends an instruction and returns its index.</summary>
        public int Emit(StackMachineInstruction instruction) { instructions.Add(instruction); return instructions.Count - 1; }
        /// <summary>Patches one emitted instruction with an in-range control-flow target.</summary>
        public bool TryPatchTarget(int instructionIndex, int targetIndex, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (instructionIndex < 0 || instructionIndex >= instructions.Count || targetIndex < 0 || targetIndex >= instructions.Count)
            { diagnostic = StackMachineDiagnostic.Create(StackMachineDiagnosticCode.InvalidControlFlow, "Instruction target patch is outside the plan."); return false; }
            instructions[instructionIndex] = instructions[instructionIndex].WithTarget(targetIndex); return true;
        }
        /// <summary>Builds an immutable plan from the currently emitted instructions.</summary>
        public StackMachinePlan Build() => new StackMachinePlan(instructions.ToArray());
    }
    /// <summary>Return-stack frame used by <see cref="StackMachineOp.Call"/> and <see cref="StackMachineOp.Exit"/>.</summary>
    public readonly struct StackMachineCallFrame
    {
        /// <summary>Instruction index to resume after the call exits.</summary>
        public readonly int ReturnInstructionPointer;
        /// <summary>Creates a return-stack frame.</summary>
        public StackMachineCallFrame(int returnInstructionPointer) { ReturnInstructionPointer = returnInstructionPointer; }
    }

    /// <summary>Common execution result and transaction lifecycle record.</summary>
    public sealed class StackMachineExecutionResult
    {
        /// <summary>Current transaction stage.</summary>
        public StackMachineTransactionStage Stage;
        /// <summary>Failure diagnostic, when execution did not succeed.</summary>
        public StackMachineDiagnostic Diagnostic;
        /// <summary>Read-only data-stack snapshot produced by the executor.</summary>
        public IReadOnlyList<StackMachineValue> DataStack;
        /// <summary>Instruction index last observed by an interpreter.</summary>
        public int InstructionPointer;
        /// <summary>Current return-stack depth observed by an interpreter.</summary>
        public int ReturnDepth;
        /// <summary>Ordered common transaction stages observed during execution.</summary>
        public readonly List<StackMachineTransactionStage> Lifecycle = new List<StackMachineTransactionStage>();
        /// <summary>Records successful domain rollback completion.</summary>
        public void MarkRollbackCompleted() { Stage = StackMachineTransactionStage.RollbackCompleted; Lifecycle.Add(StackMachineTransactionStage.RollbackCompleted); }
    }

    /// <summary>
    /// Domain-owned execution seam. Core passes only an immutable validated plan and never chooses
    /// whether the domain interprets it, executes it transactionally as bytecode, or compiles it.
    /// </summary>
    public interface IStackMachineDomainPlan
    {
        /// <summary>Immutable validated common plan from which this domain artifact was derived.</summary>
        StackMachinePlan CommonPlan { get; }
    }

    /// <summary>Compiles a common immutable plan into an immutable artifact owned by one domain.</summary>
    public interface IStackMachineDomainCompiler
    {
        /// <summary>Compiles a validated common plan using a domain-owned binding context.</summary>
        /// <param name="plan">Immutable common plan produced by <see cref="StackMachineCompiler"/>.</param>
        /// <param name="bindingContext">Domain-owned resolver for the plan's logical bindings.</param>
        /// <param name="domainPlan">Receives an immutable domain artifact when compilation succeeds.</param>
        /// <param name="diagnostic">Receives a structured common or domain diagnostic when compilation fails.</param>
        /// <returns><see langword="true"/> when the plan and context can produce a domain artifact without side effects.</returns>
        bool TryCompileDomainPlan(StackMachinePlan plan, IStackMachineBindingContext bindingContext, out IStackMachineDomainPlan domainPlan, out StackMachineDiagnostic diagnostic);
    }

    /// <summary>Domain compiler and executor contract, including cache and reevaluation policy.</summary>
    public interface IStackMachineDomainExecutor : IStackMachineDomainCompiler
    {
        /// <summary>Gets the domain-selected execution strategy.</summary>
        /// <remarks>Selecting <see cref="StackMachineDomainExecutionMode.BytecodeTransaction"/> does not itself implement resource rollback; the domain executor owns that work.</remarks>
        StackMachineDomainExecutionMode ExecutionMode { get; }
        /// <summary>Determines whether a cached domain plan remains reusable for this context.</summary>
        /// <param name="plan">Immutable common plan from which <paramref name="cachedPlan"/> was compiled.</param>
        /// <param name="bindingContext">Current domain-owned resolver context.</param>
        /// <param name="cachedPlan">Previously compiled immutable domain artifact.</param>
        /// <returns><see langword="true"/> when the cached artifact may be executed for this context.</returns>
        bool CanReusePlan(StackMachinePlan plan, IStackMachineBindingContext bindingContext, IStackMachineDomainPlan cachedPlan);
        /// <summary>Determines whether domain state requires a plan to be reevaluated.</summary>
        /// <param name="plan">Immutable common plan from which <paramref name="cachedPlan"/> was compiled.</param>
        /// <param name="bindingContext">Current domain-owned resolver context.</param>
        /// <param name="cachedPlan">Previously compiled immutable domain artifact.</param>
        /// <returns><see langword="true"/> when the cached artifact must not be used without domain reevaluation.</returns>
        bool RequiresReevaluation(StackMachinePlan plan, IStackMachineBindingContext bindingContext, IStackMachineDomainPlan cachedPlan);
        /// <summary>Executes a previously compiled domain plan and reports its common lifecycle result.</summary>
        /// <param name="domainPlan">Immutable domain artifact returned by the domain compiler.</param>
        /// <param name="bindingContext">Current domain-owned resolver context.</param>
        /// <param name="result">Receives transaction lifecycle, diagnostic, and optional interpreter state.</param>
        /// <returns><see langword="true"/> when the domain transaction succeeds.</returns>
        bool TryExecute(IStackMachineDomainPlan domainPlan, IStackMachineBindingContext bindingContext, out StackMachineExecutionResult result);
    }

    /// <summary>Domain executor base with overridable cache policy and transaction lifecycle hooks.</summary>
    public abstract class StackMachineDomainExecutorBase : IStackMachineDomainExecutor
    {
        /// <inheritdoc />
        public abstract StackMachineDomainExecutionMode ExecutionMode { get; }
        /// <inheritdoc />
        /// <remarks>The default disables domain-plan reuse. Override only when the domain can prove that its binding context and artifact are still compatible.</remarks>
        public virtual bool CanReusePlan(StackMachinePlan plan, IStackMachineBindingContext bindingContext, IStackMachineDomainPlan cachedPlan) => false;
        /// <inheritdoc />
        /// <remarks>The default is the complement of <see cref="CanReusePlan"/>. Override both methods only when the domain needs a different explicit policy.</remarks>
        public virtual bool RequiresReevaluation(StackMachinePlan plan, IStackMachineBindingContext bindingContext, IStackMachineDomainPlan cachedPlan) => !CanReusePlan(plan, bindingContext, cachedPlan);
        /// <inheritdoc />
        public abstract bool TryCompileDomainPlan(StackMachinePlan plan, IStackMachineBindingContext bindingContext, out IStackMachineDomainPlan domainPlan, out StackMachineDiagnostic diagnostic);
        /// <summary>Runs the domain plan, drives the common transaction lifecycle, and attempts rollback after failure.</summary>
        /// <param name="domainPlan">Immutable domain artifact to execute.</param><param name="bindingContext">Current domain-owned resolver context.</param><param name="result">Receives lifecycle, diagnostic, and optional interpreter state.</param>
        /// <returns><see langword="true"/> when execution succeeds.</returns>
        public bool TryExecute(IStackMachineDomainPlan domainPlan, IStackMachineBindingContext bindingContext, out StackMachineExecutionResult result)
        {
            result = new StackMachineExecutionResult { Stage = StackMachineTransactionStage.Started };
            result.Lifecycle.Add(StackMachineTransactionStage.Started);
            OnStarted(domainPlan, bindingContext, result);
            if (TryExecuteDomainPlan(domainPlan, bindingContext, result, out StackMachineDiagnostic diagnostic))
            {
                result.Stage = StackMachineTransactionStage.Succeeded; result.Lifecycle.Add(StackMachineTransactionStage.Succeeded); OnSucceeded(domainPlan, bindingContext, result); return true;
            }
            result.Stage = StackMachineTransactionStage.Failed; result.Diagnostic = diagnostic ?? StackMachineDiagnostic.CreateDomain("domain", "ExecutionFailed", "Domain execution failed."); result.Lifecycle.Add(StackMachineTransactionStage.Failed); OnFailed(domainPlan, bindingContext, result);
            if (TryRollbackDomainPlan(domainPlan, bindingContext, result, out StackMachineDiagnostic rollbackDiagnostic)) { result.MarkRollbackCompleted(); OnRollbackCompleted(domainPlan, bindingContext, result); }
            else if (rollbackDiagnostic != null) result.Diagnostic = rollbackDiagnostic;
            return false;
        }
        /// <summary>Executes domain-owned side effects for a compiled plan.</summary>
        /// <remarks>Override to perform the domain's actual work. Return a structured diagnostic instead of throwing for expected validation or execution failures. The base invokes rollback when this method returns <see langword="false"/>.</remarks>
        /// <param name="domainPlan">Immutable domain artifact to execute.</param>
        /// <param name="bindingContext">Current domain-owned resolver context.</param>
        /// <param name="result">Mutable common lifecycle result for optional interpreter state.</param>
        /// <param name="diagnostic">Receives the structured domain failure when execution fails.</param>
        /// <returns><see langword="true"/> when domain effects completed successfully.</returns>
        protected abstract bool TryExecuteDomainPlan(IStackMachineDomainPlan domainPlan, IStackMachineBindingContext bindingContext, StackMachineExecutionResult result, out StackMachineDiagnostic diagnostic);
        /// <summary>Rolls back domain-owned effects after a failed execution. The default has no rollback work.</summary>
        /// <remarks>Override only for effects that the domain can safely reverse. Returning <see langword="false"/> leaves the result at <see cref="StackMachineTransactionStage.Failed"/> and may replace its diagnostic.</remarks>
        /// <param name="domainPlan">Immutable domain artifact whose execution failed.</param>
        /// <param name="bindingContext">Current domain-owned resolver context.</param>
        /// <param name="result">Mutable common lifecycle result for rollback evidence.</param>
        /// <param name="diagnostic">Receives a structured rollback failure diagnostic.</param>
        /// <returns><see langword="true"/> when rollback completed or no rollback work was required.</returns>
        protected virtual bool TryRollbackDomainPlan(IStackMachineDomainPlan domainPlan, IStackMachineBindingContext bindingContext, StackMachineExecutionResult result, out StackMachineDiagnostic diagnostic) { diagnostic = null; return true; }
        /// <summary>Lifecycle hook raised before domain execution.</summary>
        /// <param name="domainPlan">Immutable domain artifact about to execute.</param><param name="bindingContext">Current domain-owned resolver context.</param><param name="result">Current lifecycle result at <see cref="StackMachineTransactionStage.Started"/>.</param>
        protected virtual void OnStarted(IStackMachineDomainPlan domainPlan, IStackMachineBindingContext bindingContext, StackMachineExecutionResult result) { }
        /// <summary>Lifecycle hook raised after successful domain execution.</summary>
        /// <param name="domainPlan">Immutable domain artifact that completed.</param><param name="bindingContext">Current domain-owned resolver context.</param><param name="result">Current lifecycle result at <see cref="StackMachineTransactionStage.Succeeded"/>.</param>
        protected virtual void OnSucceeded(IStackMachineDomainPlan domainPlan, IStackMachineBindingContext bindingContext, StackMachineExecutionResult result) { }
        /// <summary>Lifecycle hook raised after a domain execution failure and before rollback.</summary>
        /// <param name="domainPlan">Immutable domain artifact whose execution failed.</param><param name="bindingContext">Current domain-owned resolver context.</param><param name="result">Current lifecycle result at <see cref="StackMachineTransactionStage.Failed"/>.</param>
        protected virtual void OnFailed(IStackMachineDomainPlan domainPlan, IStackMachineBindingContext bindingContext, StackMachineExecutionResult result) { }
        /// <summary>Lifecycle hook raised after successful domain rollback.</summary>
        /// <param name="domainPlan">Immutable domain artifact that was rolled back.</param><param name="bindingContext">Current domain-owned resolver context.</param><param name="result">Current lifecycle result at <see cref="StackMachineTransactionStage.RollbackCompleted"/>.</param>
        protected virtual void OnRollbackCompleted(IStackMachineDomainPlan domainPlan, IStackMachineBindingContext bindingContext, StackMachineExecutionResult result) { }
    }

    /// <summary>Marker seam for a domain-owned logical binding resolver. Core never exposes Unity objects through it.</summary>
    public interface IStackMachineBindingContext { }

    /// <summary>Mutable data-stack view supplied only while a domain word is evaluated.</summary>
    public sealed class StackMachineExecutionContext
    {
        private readonly List<StackMachineValue> stack;
        internal StackMachineExecutionContext(List<StackMachineValue> dataStack) { stack = dataStack; }
        /// <summary>Gets a read-only view of the current data stack.</summary>
        public IReadOnlyList<StackMachineValue> DataStack => stack;
        /// <summary>Pushes a value onto the current data stack.</summary>
        public void Push(StackMachineValue value) => stack.Add(value);
        /// <summary>Removes and returns the current top data-stack value.</summary>
        public bool TryPop(out StackMachineValue value) { if (stack.Count == 0) { value = default; return false; } int last = stack.Count - 1; value = stack[last]; stack.RemoveAt(last); return true; }
    }

    /// <summary>Explicit registry for core-adjacent domain word declarations.</summary>
    public sealed class StackMachineWordRegistry
    {
        private readonly Dictionary<string, StackMachineDomainWordDefinition> domainWords = new Dictionary<string, StackMachineDomainWordDefinition>(StringComparer.OrdinalIgnoreCase);
        private readonly struct StackMachineDomainWordDefinition { public readonly string WordId; public readonly StackMachineWordSignature Signature; public StackMachineDomainWordDefinition(string wordId, StackMachineWordSignature signature) { WordId = wordId; Signature = signature; } }
        /// <summary>Registers one non-built-in token with a stable domain word identifier and stack signature.</summary>
        /// <returns><see langword="false"/> when arguments are invalid, the token is built in, or it is already registered.</returns>
        public bool TryRegisterDomainWord(string token, string wordId, StackMachineWordSignature signature)
        {
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(wordId) || signature == null || StackMachineCompiler.IsBuiltIn(token) || domainWords.ContainsKey(token)) return false;
            domainWords.Add(token, new StackMachineDomainWordDefinition(wordId, signature)); return true;
        }
        internal bool TryGetDomainWord(string token, out string wordId, out StackMachineWordSignature signature)
        {
            if (domainWords.TryGetValue(token, out StackMachineDomainWordDefinition definition)) { wordId = definition.WordId; signature = definition.Signature; return true; }
            wordId = null; signature = null; return false;
        }
    }

    /// <summary>
    /// Explicit composition seam for a known domain assembly. This is not a discovery or plugin API:
    /// the caller decides which word sets are applied. Every domain, including a no-op test stub,
    /// must declare its registration intent by overriding <see cref="RegisterWords"/>.
    /// </summary>
    public abstract class StackMachineWordSetBase
    {
        /// <summary>Registers this explicitly selected word set into a registry.</summary>
        public void RegisterInto(StackMachineWordRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            RegisterWords(registry);
        }

        /// <summary>Registers domain words, or intentionally registers none for a no-op word set.</summary>
        /// <remarks>Override this method and use <see cref="StackMachineWordRegistry.TryRegisterDomainWord"/> for every domain token. Do not use discovery or reflection-based registration.</remarks>
        /// <param name="registry">The explicitly composed registry that receives this set's declarations.</param>
        protected abstract void RegisterWords(StackMachineWordRegistry registry);

        /// <summary>Returns true only when this word set accepted and evaluated the domain instruction.</summary>
        /// <param name="instruction">Validated domain instruction whose stable word ID belongs to this word set.</param>
        /// <param name="context">Transient mutable data-stack view for the current evaluation.</param>
        /// <param name="diagnostic">Receives a structured domain diagnostic when evaluation fails.</param>
        /// <returns><see langword="true"/> when the instruction was accepted and evaluated without failure.</returns>
        public abstract bool TryEvaluateWord(StackMachineInstruction instruction, StackMachineExecutionContext context, out StackMachineDiagnostic diagnostic);
    }

    /// <summary>
    /// The pure Spec12 stub deliberately contributes no domain words. The empty override is
    /// intentional evidence that the stub executes only the Core built-in word set.
    /// </summary>
    public sealed class StackMachineStubWordSet : StackMachineWordSetBase
    {
        protected override void RegisterWords(StackMachineWordRegistry registry)
        {
            // Intentionally empty: Spec12 stub machine has no domain words.
        }

        /// <inheritdoc />
        public override bool TryEvaluateWord(StackMachineInstruction instruction, StackMachineExecutionContext context, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            return false;
        }
    }

    /// <summary>Tokenizes, validates, and compiles recipe source into an immutable common plan.</summary>
    public static class StackMachineCompiler
    {
        private static readonly Dictionary<string, StackMachineOp> Words = new Dictionary<string, StackMachineOp>(StringComparer.OrdinalIgnoreCase) { { "DEPTH", StackMachineOp.Depth }, { "DROP", StackMachineOp.Drop }, { "DUP", StackMachineOp.Dup }, { "SWAP", StackMachineOp.Swap }, { "OVER", StackMachineOp.Over }, { "ROT", StackMachineOp.Rot }, { "+", StackMachineOp.Add }, { "-", StackMachineOp.Subtract }, { "*", StackMachineOp.Multiply }, { "/", StackMachineOp.Divide }, { "NEGATE", StackMachineOp.Negate }, { "ABS", StackMachineOp.Abs }, { "MIN", StackMachineOp.Min }, { "MAX", StackMachineOp.Max }, { "1+", StackMachineOp.Increment }, { "1-", StackMachineOp.Decrement }, { "2*", StackMachineOp.Double }, { "2/", StackMachineOp.Halve }, { "=", StackMachineOp.Equal }, { "<", StackMachineOp.Less }, { ">", StackMachineOp.Greater }, { "0=", StackMachineOp.ZeroEqual }, { "0<", StackMachineOp.ZeroLess }, { "AND", StackMachineOp.And }, { "OR", StackMachineOp.Or }, { "XOR", StackMachineOp.Xor }, { "NOT", StackMachineOp.Not } };
        /// <summary>Determines whether a source token is a registered Core built-in word.</summary>
        public static bool IsBuiltIn(string token) => Words.ContainsKey(token);
        /// <summary>Compiles a recipe that uses Core built-in words only.</summary>
        public static bool TryCompile(StackMachineRecipeDocument document, out StackMachinePlan plan, out StackMachineDiagnostic diagnostic) => TryCompile(document, null, out plan, out diagnostic);
        /// <summary>Compiles a validated recipe with Core built-ins and explicitly registered domain words.</summary>
        public static bool TryCompile(StackMachineRecipeDocument document, StackMachineWordRegistry registry, out StackMachinePlan plan, out StackMachineDiagnostic diagnostic) { plan = null; if (!StackMachineRecipeSerialization.TryValidateDocument(document, out diagnostic)) return false; if (string.IsNullOrWhiteSpace(document.wordSource)) { diagnostic = StackMachineDiagnostic.Create(StackMachineDiagnosticCode.EmptySource, "Recipe source is empty."); return false; } var list = new List<StackMachineInstruction>(); var tokens = document.wordSource.Split((char[])null, StringSplitOptions.RemoveEmptyEntries); var bindings = new HashSet<string>(); if (document.bindings != null) foreach (var b in document.bindings) bindings.Add(b.logicalName); for (var i = 0; i < tokens.Length; i++) { var t = tokens[i]; if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && !double.IsNaN(n) && !double.IsInfinity(n)) list.Add(new StackMachineInstruction(StackMachineOp.PushNumber, n)); else if (string.Equals(t, "TRUE", StringComparison.OrdinalIgnoreCase)) list.Add(new StackMachineInstruction(StackMachineOp.PushBoolean, boolean: true)); else if (string.Equals(t, "FALSE", StringComparison.OrdinalIgnoreCase)) list.Add(new StackMachineInstruction(StackMachineOp.PushBoolean)); else if (t.StartsWith("$", StringComparison.Ordinal)) { var name = t.Substring(1); if (!bindings.Contains(name)) { diagnostic = StackMachineDiagnostic.Create(StackMachineDiagnosticCode.InvalidBinding, "Unknown logical binding '" + name + "'.", i); return false; } list.Add(new StackMachineInstruction(StackMachineOp.PushBinding, binding: name)); } else if (Words.TryGetValue(t, out var op)) list.Add(new StackMachineInstruction(op)); else if (registry != null && registry.TryGetDomainWord(t, out var id, out var signature)) list.Add(new StackMachineInstruction(StackMachineOp.DomainWord, wordId: id, signature: signature)); else { diagnostic = StackMachineDiagnostic.Create(StackMachineDiagnosticCode.UnknownToken, "Unknown token '" + t + "'.", i); return false; } } plan = new StackMachinePlan(list.ToArray()); return TryValidateBuiltInStack(plan, out diagnostic); }
        private static bool TryValidateBuiltInStack(StackMachinePlan plan, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null; var tags = new List<StackMachineValueTag>();
            for (int i = 0; i < plan.Instructions.Count; i++) { var instruction = plan.Instructions[i]; var op = instruction.Op; if(op==StackMachineOp.PushNumber) { tags.Add(StackMachineValueTag.Number); continue; } if(op==StackMachineOp.PushBoolean){tags.Add(StackMachineValueTag.Boolean);continue;} if(op==StackMachineOp.PushBinding){tags.Add(StackMachineValueTag.ResourceHandle);continue;} if(op==StackMachineOp.DomainWord) { if (instruction.Signature == null) { diagnostic=StackMachineDiagnostic.Create(StackMachineDiagnosticCode.TypeMismatch,"Domain word signature is required.",i);return false; } if(tags.Count<instruction.Signature.Inputs.Count){diagnostic=StackMachineDiagnostic.Create(StackMachineDiagnosticCode.StackUnderflow,"Compile-time stack underflow.",i);return false;} for(int j=0;j<instruction.Signature.Inputs.Count;j++)if(tags[tags.Count-1-j]!=instruction.Signature.Inputs[instruction.Signature.Inputs.Count-1-j]){diagnostic=StackMachineDiagnostic.Create(StackMachineDiagnosticCode.TypeMismatch,"Compile-time domain word signature mismatch.",i);return false;} tags.RemoveRange(tags.Count-instruction.Signature.Inputs.Count,instruction.Signature.Inputs.Count); for(int j=0;j<instruction.Signature.Outputs.Count;j++)tags.Add(instruction.Signature.Outputs[j]); continue; } if(op==StackMachineOp.Call||op==StackMachineOp.Exit) continue; if(op==StackMachineOp.Depth){tags.Add(StackMachineValueTag.Number);continue;} int need=op==StackMachineOp.Drop||op==StackMachineOp.Dup||op==StackMachineOp.Negate||op==StackMachineOp.Abs||op==StackMachineOp.Increment||op==StackMachineOp.Decrement||op==StackMachineOp.Double||op==StackMachineOp.Halve||op==StackMachineOp.ZeroEqual||op==StackMachineOp.ZeroLess||op==StackMachineOp.Not?1:op==StackMachineOp.Rot?3:2; if(tags.Count<need){diagnostic=StackMachineDiagnostic.Create(StackMachineDiagnosticCode.StackUnderflow,"Compile-time stack underflow.",i);return false;} if(op==StackMachineOp.Drop){tags.RemoveAt(tags.Count-1);continue;} if(op==StackMachineOp.Dup){tags.Add(tags[tags.Count-1]);continue;} if(op==StackMachineOp.Swap){int n=tags.Count;var x=tags[n-1];tags[n-1]=tags[n-2];tags[n-2]=x;continue;} if(op==StackMachineOp.Over){tags.Add(tags[tags.Count-2]);continue;} if(op==StackMachineOp.Rot){int n=tags.Count;var x=tags[n-3];tags[n-3]=tags[n-2];tags[n-2]=tags[n-1];tags[n-1]=x;continue;} bool boolean=op==StackMachineOp.And||op==StackMachineOp.Or||op==StackMachineOp.Xor||op==StackMachineOp.Not; bool comparison=op==StackMachineOp.Equal||op==StackMachineOp.Less||op==StackMachineOp.Greater||op==StackMachineOp.ZeroEqual||op==StackMachineOp.ZeroLess; var required=boolean?StackMachineValueTag.Boolean:StackMachineValueTag.Number; for(int j=0;j<need;j++)if(tags[tags.Count-1-j]!=required && !(op==StackMachineOp.Equal && (tags[tags.Count-1-j]==StackMachineValueTag.Number||tags[tags.Count-1-j]==StackMachineValueTag.Boolean))){diagnostic=StackMachineDiagnostic.Create(StackMachineDiagnosticCode.TypeMismatch,"Compile-time stack type mismatch.",i);return false;} if(op==StackMachineOp.Equal&&tags[tags.Count-1]!=tags[tags.Count-2]){diagnostic=StackMachineDiagnostic.Create(StackMachineDiagnosticCode.TypeMismatch,"= requires matching tags.",i);return false;} tags.RemoveRange(tags.Count-need,need);tags.Add(comparison?StackMachineValueTag.Boolean:required); }
            return true;
        }
    }
}
