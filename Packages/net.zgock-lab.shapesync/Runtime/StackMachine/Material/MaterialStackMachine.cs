// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine.Humanoid;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>Terminal outcome of one Material StackMachine orchestration request.</summary>
    public enum MaterialStackMachineResultCode
    {
        /// <summary>Every requested entry was committed through its existing Material Attacher.</summary>
        Applied,
        /// <summary>At least one requested semantic was intentionally ignored by its Adapter while all commits succeeded.</summary>
        PartialApplied,
        /// <summary>The request was rejected and every still-escrowed delivery was released.</summary>
        Rejected
    }

    /// <summary>Terminal result of a Material StackMachine request.</summary>
    public sealed class MaterialStackMachineResult
    {
        internal MaterialStackMachineResult(MaterialStackMachineResultCode code, StackMachineDiagnostic diagnostic)
        { Code = code; Diagnostic = diagnostic; }

        /// <summary>Gets the terminal request outcome.</summary>
        public MaterialStackMachineResultCode Code { get; }
        /// <summary>Gets the structured rejection diagnostic, or <see langword="null"/> after success.</summary>
        public StackMachineDiagnostic Diagnostic { get; }
    }

    /// <summary>Owns one in-flight Material StackMachine request until all Texture recipes, DryRuns, and entry commits complete.</summary>
    /// <remarks>Texture deliveries are owned by this operation during escrow and transfer through an existing <see cref="MaterialAttacher"/> into the entry-local Material Proxy at commit.</remarks>
    public sealed class MaterialStackMachineOperation : IDisposable
    {
        internal sealed class Item
        {
            internal MaterialStackMachineBlock block;
            internal MaterialBindingContext.Binding binding;
            internal MaterialProxySemanticValues values;
            internal TextureExecutionHandle textureHandle;
            internal TextureDelivery delivery;
            internal MaterialAttacherDryRunPlan dryRun;
            internal MaterialAttacherResetDryRunPlan resetDryRun;
        }

        private readonly List<Item> items;
        private bool terminal;
        private bool disposed;
        private MaterialStackMachineResult result;

        internal MaterialStackMachineOperation(List<Item> items) => this.items = items;

        /// <summary>Raised once when the request reaches a terminal success or rejection state.</summary>
        public event Action<MaterialStackMachineResult> Completed;
        /// <summary>Gets whether the request has completed.</summary>
        public bool IsCompleted => terminal;
        /// <summary>Gets the terminal result after <see cref="IsCompleted"/> becomes true.</summary>
        public MaterialStackMachineResult Result => result;

        internal void Tick()
        {
            if (terminal) return;
            for (int i = 0; i < items.Count; i++)
            {
                TextureExecutionHandle handle = items[i].textureHandle;
                if (handle != null && !handle.IsCompleted) return;
            }

            for (int i = 0; i < items.Count; i++)
            {
                Item item = items[i];
                if (item.textureHandle == null) continue;
                if (!item.textureHandle.Succeeded || item.textureHandle.Result == null || !item.textureHandle.Result.TryTakeDelivery(out item.delivery))
                {
                    Reject(item.textureHandle == null ? null : item.textureHandle.Diagnostic ?? Failure("TextureDeliveryMissing", "Texture execution completed without a delivery.", item.block.BindingName));
                    return;
                }
                item.values.applyBaseColorTexture = true;
                item.values.baseColorTexture = item.delivery.Texture;
                item.textureHandle.Dispose();
                item.textureHandle = null;
            }

            // No Attacher has been touched before this point: every Texture recipe has completed and every candidate is escrowed here.
            for (int i = 0; i < items.Count; i++)
            {
                Item item = items[i];
                if (item.block.IsReset)
                {
                    if (!item.binding.Attacher.TryDryRunReset(item.binding.EntryName, out item.resetDryRun, out MaterialProxyDiagnostic resetDiagnostic))
                    {
                        Reject(FromProxy("ResetDryRunRejected", "Material Attacher rejected an entry during the all-entry reset DryRun phase.", item.block.BindingName, resetDiagnostic));
                        return;
                    }
                    continue;
                }
                if (!item.binding.Attacher.TryDryRun(item.binding.EntryName, item.values, out item.dryRun, out MaterialProxyDiagnostic proxyDiagnostic))
                {
                    Reject(FromProxy("DryRunRejected", "Material Attacher rejected an entry during the all-entry DryRun phase.", item.block.BindingName, proxyDiagnostic));
                    return;
                }
            }

            bool partial = false;
            for (int i = 0; i < items.Count; i++)
            {
                Item item = items[i];
                if (item.block.IsReset)
                {
                    if (!item.binding.Attacher.TryCommitReset(item.resetDryRun, out MaterialAttacherResult resetResult))
                    {
                        Reject(FromProxy("ResetCommitRejected", "Material Attacher rejected a reset after the all-entry DryRun phase.", item.block.BindingName, resetResult.diagnostic));
                        return;
                    }
                    continue;
                }
                TextureDelivery delivery = item.delivery;
                item.delivery = null; // ownership transfers through Attacher to the entry-local Proxy.
                if (!item.binding.Attacher.TryCommit(item.dryRun, delivery, out MaterialAttacherResult result))
                {
                    Reject(FromProxy("CommitRejected", "Material Attacher rejected an entry after the all-entry DryRun phase.", item.block.BindingName, result.diagnostic));
                    return;
                }
                partial |= result.code == MaterialAttacherResultCode.PartialApplied;
            }
            Complete(new MaterialStackMachineResult(partial ? MaterialStackMachineResultCode.PartialApplied : MaterialStackMachineResultCode.Applied, null));
        }

        internal void Reject(StackMachineDiagnostic diagnostic)
        {
            if (terminal) return;
            DisposeEscrow();
            Complete(new MaterialStackMachineResult(MaterialStackMachineResultCode.Rejected, diagnostic));
        }

        /// <summary>Terminates a non-terminal request and releases every still-escrowed Texture handle and delivery.</summary>
        /// <remarks>
        /// Disposal reports <c>OperationDisposed</c> through <see cref="Completed"/> when the operation has not reached a terminal result.
            /// A delivery already transferred through an Attacher into a Proxy is not owned by this operation and is therefore not released here.
        /// </remarks>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (!terminal) Reject(Failure("OperationDisposed", "Material StackMachine request was disposed before completion.", null));
        }

        private void Complete(MaterialStackMachineResult result)
        {
            terminal = true;
            this.result = result;
            Completed?.Invoke(result);
        }

        private void DisposeEscrow()
        {
            for (int i = 0; i < items.Count; i++)
            {
                Item item = items[i];
                item.textureHandle?.Dispose();
                item.textureHandle = null;
                item.delivery?.Dispose();
                item.delivery = null;
            }
        }

        private static StackMachineDiagnostic Failure(string code, string message, string bindingName)
            => StackMachineDiagnostic.CreateDomain("material", code, message, bindingName: bindingName);
        private static StackMachineDiagnostic FromProxy(string code, string message, string bindingName, MaterialProxyDiagnostic proxy)
            => StackMachineDiagnostic.CreateDomain("material", code, message, bindingName: bindingName, detail: proxy.code + ": " + proxy.message);

        internal static Item CreateItem(MaterialStackMachineBlock block, MaterialBindingContext.Binding binding)
        {
            var values = new MaterialProxySemanticValues
            {
                applyColor = block.HasColor,
                color = block.Color,
                applyUvTransform = block.HasUvTransform,
                uvScale = block.UvScale,
                uvOffset = block.UvOffset
            };
            return new Item { block = block, binding = binding, values = values };
        }

        internal bool TrySetTextureHandle(MaterialStackMachineBlock block, TextureExecutionHandle handle)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (!ReferenceEquals(items[i].block, block)) continue;
                items[i].textureHandle = handle;
                return true;
            }

            return false;
        }
    }

    /// <summary>Material-domain orchestrator that escrows all Texture results before invoking existing one-entry Material Attacher transactions.</summary>
    /// <remarks>
    /// This component owns only cross-material ordering and escrow. It does not mutate a Material Proxy or add transaction state to lower layers.
    /// A request containing a <c>TEXTURE</c> block resolves the active scene Host through <see cref="TextureStaticMachineFactory"/>;
    /// color-only and UV-only requests do not create or resolve a Texture Host.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class MaterialStackMachine : MonoBehaviour
    {
        [SerializeField] private MaterialAttacher materialAttacher;
        [SerializeField] private TextureBindingTemplate textureBindingTemplate;
        private MaterialStackMachineOperation active;

        /// <summary>Gets or sets the co-located Attacher that performs existing one-entry commits.</summary>
        public MaterialAttacher MaterialAttacher { get => materialAttacher; set => materialAttacher = value; }
        /// <summary>Gets or sets the shared Texture StackMachine dictionary used by every <c>TEXTURE</c> block.</summary>
        public TextureBindingTemplate TextureBindingTemplate { get => textureBindingTemplate; set => textureBindingTemplate = value; }
        /// <summary>Gets whether this StackMachine is currently escrowing or committing one request.</summary>
        public bool IsBusy => active != null && !active.IsCompleted;

        /// <summary>Ensures the co-located one-entry Material Attacher required by this Machine is available.</summary>
        /// <param name="diagnostic">A structured readiness failure.</param>
        /// <returns><see langword="true"/> when this Machine may accept a Material request.</returns>
        public bool TryEnsureReady(out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (materialAttacher == null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("material", "MaterialAttacherRequired", "Material StackMachine requires a co-located MaterialAttacher.");
                return false;
            }
            if (materialAttacher.gameObject != gameObject)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("material", "MaterialAttacherNotCoLocated", "Material StackMachine requires its MaterialAttacher on the same GameObject.");
                return false;
            }
            return true;
        }

        /// <summary>Accepts one Spec15.3 Material payload using its shared document binding.</summary>
        /// <param name="payload">The Figure-owned full document or a target-local payload.</param>
        /// <param name="operation">The accepted Figure-local operation, when one exists. Outfit target operations are self-owned by their target Machines and are not aggregated here.</param>
        /// <param name="diagnostic">A structured pre-dispatch configuration, parser, or binding rejection. Target-local failures are logged as warnings and do not populate this value.</param>
        /// <returns><see langword="true"/> when an empty payload no-ops or payload-level validation succeeds; individual Figure or Outfit target failures are target-local aborts.</returns>
        public bool TryAcceptRecipePayload(ShapeSyncDocument payload, out MaterialStackMachineOperation operation, out StackMachineDiagnostic diagnostic)
        {
            operation = null;
            diagnostic = null;
            if (payload == null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("material", "PayloadRequired", "MaterialStackMachine requires a ShapeSyncDocument payload.");
                return false;
            }
            if (payload.MaterialRecipe == null || string.IsNullOrWhiteSpace(payload.MaterialRecipe.wordSource)) return true;
            if (textureBindingTemplate != null && payload.MaterialBinding != null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("material", "ConfigurationConflict", "MaterialStackMachine cannot use TextureBindingTemplate and a ShapeSyncDocument MaterialBinding together.");
                return false;
            }
            if (payload.MaterialBinding == null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("material", "BindingRequired", "A non-empty Material document payload requires MaterialBinding.");
                return false;
            }
            if (!MaterialTargetScopeParser.TryParse(payload.MaterialRecipe.wordSource, out IReadOnlyList<MaterialTargetSource> targets, out diagnostic)) return false;
            for (int i = 0; i < targets.Count; i++)
            {
                MaterialTargetSource target = targets[i];
                if (target.OutfitRegistryId == null)
                {
                    if (!TryExecuteDocumentBinding(target.Source, payload.MaterialBinding, out MaterialStackMachineOperation figureOperation, out StackMachineDiagnostic figureDiagnostic))
                    {
                        Debug.LogWarning("Material StackMachine skipped Figure target. " + FormatDiagnostic(figureDiagnostic), this);
                        continue;
                    }
                    operation = figureOperation;
                    continue;
                }
                OutfitAttacher outfitAttacher = GetComponent<OutfitAttacher>();
                StackMachineDiagnostic targetDiagnostic = null;
                if (outfitAttacher == null || !outfitAttacher.TryGetAttachedMaterialStackMachine(target.OutfitRegistryId, out MaterialStackMachine outfitMachine, out targetDiagnostic))
                {
                    Debug.LogWarning("Material StackMachine skipped Outfit target " + target.OutfitRegistryId + ". " + FormatDiagnostic(targetDiagnostic), this);
                    continue;
                }
                if (!ShapeSyncDocument.TryCreateSnapshot(payload, out ShapeSyncDocument targetPayload, out targetDiagnostic))
                {
                    Debug.LogWarning("Material StackMachine skipped Outfit target " + target.OutfitRegistryId + ". " + FormatDiagnostic(targetDiagnostic), outfitMachine);
                    continue;
                }
                targetPayload.MaterialRecipe.wordSource = target.Source;
                targetPayload.MeshRecipe = null;
                targetPayload.MeshBinding = null;
                if (!outfitMachine.TryAcceptRecipePayload(targetPayload, out _, out targetDiagnostic))
                {
                    Debug.LogWarning("Material StackMachine skipped Outfit target " + target.OutfitRegistryId + ". " + FormatDiagnostic(targetDiagnostic), outfitMachine);
                }
            }
            return true;
        }

        /// <summary>Accepts one Material payload and returns a non-owning completion aggregate for its fixed target set.</summary>
        public bool TryAcceptRecipePayloadWithCompletion(ShapeSyncDocument payload, out MaterialStackMachineDispatchOperation operation, out StackMachineDiagnostic diagnostic)
        {
            operation = null; diagnostic = null;
            if (payload == null) { diagnostic = StackMachineDiagnostic.CreateDomain("material", "PayloadRequired", "MaterialStackMachine requires a ShapeSyncDocument payload."); return false; }
            if (payload.MaterialRecipe == null || string.IsNullOrWhiteSpace(payload.MaterialRecipe.wordSource)) return true;
            if (textureBindingTemplate != null && payload.MaterialBinding != null) { diagnostic = StackMachineDiagnostic.CreateDomain("material", "ConfigurationConflict", "MaterialStackMachine cannot use TextureBindingTemplate and a ShapeSyncDocument MaterialBinding together."); return false; }
            if (payload.MaterialBinding == null) { diagnostic = StackMachineDiagnostic.CreateDomain("material", "BindingRequired", "A non-empty Material document payload requires MaterialBinding."); return false; }
            if (!MaterialTargetScopeParser.TryParse(payload.MaterialRecipe.wordSource, out IReadOnlyList<MaterialTargetSource> targets, out diagnostic)) return false;
            if (targets.Count == 0) return true;
            operation = new MaterialStackMachineDispatchOperation(targets.Count);
            for (int i = 0; i < targets.Count; i++)
            {
                MaterialTargetSource target = targets[i]; string registryId = target.OutfitRegistryId ?? string.Empty;
                if (target.OutfitRegistryId == null)
                {
                    if (TryExecuteDocumentBinding(target.Source, payload.MaterialBinding, out MaterialStackMachineOperation child, out StackMachineDiagnostic childDiagnostic)) operation.Observe(i, registryId, child);
                    else { Debug.LogWarning("Material StackMachine skipped Figure target. " + FormatDiagnostic(childDiagnostic), this); operation.Reject(i, registryId, childDiagnostic); }
                    continue;
                }
                OutfitAttacher outfitAttacher = GetComponent<OutfitAttacher>();
                StackMachineDiagnostic targetDiagnostic = null;
                if (outfitAttacher == null || !outfitAttacher.TryGetAttachedMaterialStackMachine(target.OutfitRegistryId, out MaterialStackMachine outfitMachine, out targetDiagnostic)) { Debug.LogWarning("Material StackMachine skipped Outfit target " + registryId + ". " + FormatDiagnostic(targetDiagnostic), this); operation.Reject(i, registryId, targetDiagnostic); continue; }
                if (!ShapeSyncDocument.TryCreateSnapshot(payload, out ShapeSyncDocument targetPayload, out targetDiagnostic)) { Debug.LogWarning("Material StackMachine skipped Outfit target " + registryId + ". " + FormatDiagnostic(targetDiagnostic), outfitMachine); operation.Reject(i, registryId, targetDiagnostic); continue; }
                targetPayload.MaterialRecipe.wordSource = target.Source; targetPayload.MeshRecipe = null; targetPayload.MeshBinding = null;
                if (outfitMachine.TryAcceptRecipePayloadWithCompletion(targetPayload, out MaterialStackMachineDispatchOperation childOperation, out targetDiagnostic))
                {
                    if (childOperation != null && childOperation.TargetCompletions.Count != 1)
                    {
                        targetDiagnostic = StackMachineDiagnostic.CreateDomain("material", "NestedTargetCountInvalid", "Outfit target payload must resolve exactly one Material target.");
                        Debug.LogWarning("Material StackMachine skipped Outfit target " + registryId + ". " + FormatDiagnostic(targetDiagnostic), outfitMachine);
                        operation.Reject(i, registryId, targetDiagnostic);
                    }
                    else operation.Observe(i, registryId, childOperation);
                }
                else { Debug.LogWarning("Material StackMachine skipped Outfit target " + registryId + ". " + FormatDiagnostic(targetDiagnostic), outfitMachine); operation.Reject(i, registryId, targetDiagnostic); }
            }
            operation.Seal();
            return true;
        }

        private void Update()
        {
            active?.Tick();
            if (active != null && active.IsCompleted) active = null;
        }

        private void OnDisable() { active?.Dispose(); active = null; }
        private void OnDestroy() { active?.Dispose(); active = null; }

        /// <summary>Parses and starts one Material recipe request without touching an Attacher until every nested Texture recipe has completed.</summary>
        /// <param name="wordSource">Complete Material StackMachine source.</param>
        /// <param name="operation">The in-flight operation on success; subscribe to <see cref="MaterialStackMachineOperation.Completed"/> for its terminal result.</param>
        /// <param name="diagnostic">Structured parser, binding, compilation, or queue rejection diagnostic.</param>
        /// <returns><see langword="true"/> when all nested Texture recipes were accepted for execution.</returns>
        public bool TryExecute(string wordSource, out MaterialStackMachineOperation operation, out StackMachineDiagnostic diagnostic)
        {
            operation = null;
            if (IsBusy) { diagnostic = StackMachineDiagnostic.CreateDomain("material", "MachineBusy", "Material StackMachine already owns an in-flight request."); return false; }
            if (materialAttacher == null) { diagnostic = StackMachineDiagnostic.CreateDomain("material", "MaterialAttacherRequired", "Material StackMachine requires a co-located MaterialAttacher."); return false; }
            if (materialAttacher.gameObject != gameObject) { diagnostic = StackMachineDiagnostic.CreateDomain("material", "MaterialAttacherNotCoLocated", "Material StackMachine requires its MaterialAttacher on the same GameObject."); return false; }
            if (!MaterialStackMachineCorePlan.TryCreate(new MaterialRecipeDocument { wordSource = wordSource }, out MaterialStackMachineCorePlan corePlan, out diagnostic)) return false;
            MaterialStackMachinePlan plan = corePlan.CommonPlan;
            if (!MaterialBindingContext.TryCreate(materialAttacher, out MaterialBindingContext context, out diagnostic)) return false;

            bool requiresTextureHost = false;
            for (int i = 0; i < plan.Blocks.Count; i++) requiresTextureHost |= plan.Blocks[i].TextureSource != null;
            TextureStackMachineHost textureHost = null;
            if (requiresTextureHost && !TextureStaticMachineFactory.TryGetTSM(out textureHost, out diagnostic)) return false;

            var items = new List<MaterialStackMachineOperation.Item>();
            for (int i = 0; i < plan.Blocks.Count; i++)
            {
                MaterialStackMachineBlock block = plan.Blocks[i];
                if (block.IsReset)
                {
                    if (!context.TryGetResetBindings(out IReadOnlyList<MaterialBindingContext.Binding> resetBindings, out diagnostic)) return false;
                    for (int resetIndex = 0; resetIndex < resetBindings.Count; resetIndex++) items.Add(MaterialStackMachineOperation.CreateItem(block, resetBindings[resetIndex]));
                    continue;
                }
                if (!context.TryGetBinding(block.BindingName, out MaterialBindingContext.Binding binding)) { diagnostic = StackMachineDiagnostic.CreateDomain("material", "BindingMissing", "No Material binding exists for a MATERIAL block.", bindingName: block.BindingName); return false; }
                if (block.TextureSource != null && textureBindingTemplate == null && !ContainsBindingToken(block.TextureSource, "current")) { diagnostic = StackMachineDiagnostic.CreateDomain("material", "TextureTemplateRequired", "A MATERIAL TEXTURE block requires the shared TextureBindingTemplate.", bindingName: block.BindingName); return false; }
                items.Add(MaterialStackMachineOperation.CreateItem(block, binding));
            }

            var candidate = new MaterialStackMachineOperation(items);
            TextureExecutor executor = requiresTextureHost ? new TextureExecutor(textureHost) : null;
            for (int i = 0; i < plan.Blocks.Count; i++)
            {
                MaterialStackMachineBlock block = plan.Blocks[i];
                if (block.TextureSource == null) continue;
                if (!TryCreateTextureStub(block, textureBindingTemplate, materialAttacher.Proxy, out TextureRecipeStub stub, out StackMachineDiagnostic textureDiagnostic) || !executor.TryExecute(stub, textureHost.CreateOrigin(), out TextureExecutionHandle handle, out textureDiagnostic))
                {
                    diagnostic = textureDiagnostic;
                    candidate.Reject(textureDiagnostic);
                    return false;
                }
                if (!candidate.TrySetTextureHandle(block, handle))
                {
                    handle.Dispose();
                    diagnostic = StackMachineDiagnostic.CreateDomain("material", "TextureItemMissing", "Material StackMachine could not associate a Texture execution with its transaction item.", bindingName: block.BindingName);
                    candidate.Reject(diagnostic);
                    return false;
                }
            }
            active = candidate;
            operation = candidate;
            diagnostic = null;
            return true;
        }

        /// <summary>Replaces an in-flight pre-commit Material request with the supplied latest request.</summary>
        /// <param name="wordSource">Complete Material StackMachine source for the latest request.</param>
        /// <param name="operation">The latest in-flight operation on success.</param>
        /// <param name="diagnostic">Structured parser, binding, compilation, or queue rejection diagnostic.</param>
        /// <returns><see langword="true"/> when the latest request was accepted.</returns>
        /// <remarks>
        /// The replaced operation is disposed before the new request is parsed or queued. Its escrowed deliveries and Texture handles are released or cancelled;
        /// no Attacher mutation can have occurred while an operation is still in this component's active slot.
        /// </remarks>
        public bool TryExecuteLatest(string wordSource, out MaterialStackMachineOperation operation, out StackMachineDiagnostic diagnostic)
        {
            if (IsBusy)
            {
                active.Dispose();
                active = null;
            }
            return TryExecute(wordSource, out operation, out diagnostic);
        }

        private bool TryExecuteDocumentBinding(string wordSource, MaterialBinding binding, out MaterialStackMachineOperation operation, out StackMachineDiagnostic diagnostic)
        {
            operation = null;
            if (IsBusy) { diagnostic = StackMachineDiagnostic.CreateDomain("material", "MachineBusy", "Material StackMachine already owns an in-flight request."); return false; }
            if (!TryEnsureReady(out diagnostic)) return false;
            if (!MaterialStackMachineCorePlan.TryCreate(new MaterialRecipeDocument { wordSource = wordSource }, out MaterialStackMachineCorePlan corePlan, out diagnostic)) return false;
            MaterialStackMachinePlan plan = corePlan.CommonPlan;
            if (!MaterialBindingContext.TryCreate(materialAttacher, out MaterialBindingContext context, out diagnostic)) return false;

            bool requiresTextureHost = false;
            for (int i = 0; i < plan.Blocks.Count; i++) requiresTextureHost |= plan.Blocks[i].TextureSource != null;
            TextureStackMachineHost textureHost = null;
            if (requiresTextureHost && !TextureStaticMachineFactory.TryGetTSM(out textureHost, out diagnostic)) return false;

            var items = new List<MaterialStackMachineOperation.Item>();
            for (int i = 0; i < plan.Blocks.Count; i++)
            {
                MaterialStackMachineBlock block = plan.Blocks[i];
                if (block.IsReset)
                {
                    if (!context.TryGetResetBindings(out IReadOnlyList<MaterialBindingContext.Binding> resetBindings, out diagnostic)) return false;
                    for (int resetIndex = 0; resetIndex < resetBindings.Count; resetIndex++) items.Add(MaterialStackMachineOperation.CreateItem(block, resetBindings[resetIndex]));
                    continue;
                }
                if (!context.TryGetBinding(block.BindingName, out MaterialBindingContext.Binding resolved)) { diagnostic = StackMachineDiagnostic.CreateDomain("material", "BindingMissing", "No Material binding exists for a MATERIAL block.", bindingName: block.BindingName); return false; }
                items.Add(MaterialStackMachineOperation.CreateItem(block, resolved));
            }

            var candidate = new MaterialStackMachineOperation(items);
            TextureExecutor executor = requiresTextureHost ? new TextureExecutor(textureHost) : null;
            for (int i = 0; i < plan.Blocks.Count; i++)
            {
                MaterialStackMachineBlock block = plan.Blocks[i];
                if (block.TextureSource == null) continue;
                if (!TryCreateTextureStub(block, binding, materialAttacher.Proxy, out TextureRecipeStub stub, out StackMachineDiagnostic textureDiagnostic) || !executor.TryExecute(stub, textureHost.CreateOrigin(), out TextureExecutionHandle handle, out textureDiagnostic))
                {
                    diagnostic = textureDiagnostic;
                    candidate.Reject(textureDiagnostic);
                    return false;
                }
                if (!candidate.TrySetTextureHandle(block, handle))
                {
                    handle.Dispose();
                    diagnostic = StackMachineDiagnostic.CreateDomain("material", "TextureItemMissing", "Material StackMachine could not associate a Texture execution with its transaction item.", bindingName: block.BindingName);
                    candidate.Reject(diagnostic);
                    return false;
                }
            }
            active = candidate;
            operation = candidate;
            diagnostic = null;
            return true;
        }


        private static bool TryCreateTextureStub(MaterialStackMachineBlock block, TextureBindingTemplate template, MaterialProxy proxy, out TextureRecipeStub stub, out StackMachineDiagnostic diagnostic)
        {
            if (ContainsBindingToken(block.TextureSource, "current"))
            {
                if (proxy == null)
                {
                    stub = null;
                    diagnostic = StackMachineDiagnostic.CreateDomain("material", "CurrentTextureProxyRequired", "A mask-only canvas recipe requires the co-located Material Proxy.", bindingName: block.BindingName);
                    return false;
                }
                var dynamicDocument = new MaterialRecipeDocument { wordSource = block.TextureSource };
                var dynamicEntries = new List<TextureBindingEntry>();
                IReadOnlyList<TextureTemplateEntry> dynamicBindings = template == null ? null : template.Bindings;
                if (dynamicBindings == null)
                {
                    dynamicEntries.Add(new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall });
                    dynamicDocument.bindings.Add(new StackMachineBindingDeclaration { logicalName = "out", declaredKind = StackMachineBindingKind.Resource });
                }
                for (int i = 0; dynamicBindings != null && i < dynamicBindings.Count; i++)
                {
                    TextureTemplateEntry binding = dynamicBindings[i];
                    if (binding == null || string.IsNullOrWhiteSpace(binding.word) || binding.word == "current")
                    {
                        stub = null;
                        diagnostic = StackMachineDiagnostic.CreateDomain("material", "TextureTemplateInvalid", "TextureBindingTemplate cannot declare the reserved current binding.", bindingName: block.BindingName);
                        return false;
                    }
                    dynamicEntries.Add(new TextureBindingEntry { logicalName = binding.word, kind = binding.kind, sourceTexture = binding.texture });
                    dynamicDocument.bindings.Add(new StackMachineBindingDeclaration { logicalName = binding.word, declaredKind = StackMachineBindingKind.Resource });
                }
                if (!proxy.TryGetCurrentBaseColorTexture(block.BindingName, out Texture currentTexture, out MaterialProxyDiagnostic proxyDiagnostic))
                {
                    stub = null;
                    diagnostic = StackMachineDiagnostic.CreateDomain("material", "CurrentTextureUnavailable", proxyDiagnostic.message, bindingName: block.BindingName);
                    return false;
                }
                dynamicEntries.Add(new TextureBindingEntry { logicalName = "current", kind = TextureBindingKind.SourceTexture, sourceTexture = currentTexture });
                dynamicDocument.bindings.Add(new StackMachineBindingDeclaration { logicalName = "current", declaredKind = StackMachineBindingKind.Resource });
                var dynamicStub = new TextureRecipeStub(dynamicDocument, dynamicEntries.ToArray());
                if (!TextureBindingContext.TryCreate(dynamicStub, out _, out diagnostic)) { stub = null; return false; }
                stub = dynamicStub;
                return true;
            }
            var document = new MaterialRecipeDocument { wordSource = block.TextureSource };
            IReadOnlyList<TextureTemplateEntry> bindings = template.Bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                TextureTemplateEntry binding = bindings[i];
                if (binding == null || binding.word == "current")
                {
                    stub = null;
                    diagnostic = StackMachineDiagnostic.CreateDomain("material", "TextureTemplateInvalid", "Material TEXTURE requires complete TextureBindingTemplate entries and reserves the current logical name.", bindingName: block.BindingName);
                    return false;
                }
                document.bindings.Add(new StackMachineBindingDeclaration { logicalName = binding.word, declaredKind = StackMachineBindingKind.Resource });
            }
            return template.TryCreateStub(document, out stub, out diagnostic);
        }

        private static bool TryCreateTextureStub(MaterialStackMachineBlock block, MaterialBinding binding, MaterialProxy proxy, out TextureRecipeStub stub, out StackMachineDiagnostic diagnostic)
        {
            stub = null;
            var document = new MaterialRecipeDocument { wordSource = block.TextureSource };
            var entries = new List<TextureBindingEntry> { new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall } };
            document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "out", declaredKind = StackMachineBindingKind.Resource });
            IReadOnlyList<MaterialTextureBindingEntry> textures = binding.Textures;
            for (int i = 0; i < textures.Count; i++)
            {
                MaterialTextureBindingEntry texture = textures[i];
                if (texture == null || string.IsNullOrWhiteSpace(texture.logicalName) || texture.sourceTexture == null || texture.logicalName == "out" || texture.logicalName == "current")
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("material", "TextureBindingInvalid", "MaterialBinding source textures require a non-reserved logical name and Texture2D; 'out' and 'current' are reserved.", bindingName: block.BindingName);
                    return false;
                }
                entries.Add(new TextureBindingEntry { logicalName = texture.logicalName, kind = TextureBindingKind.SourceTexture, sourceTexture = texture.sourceTexture });
                document.bindings.Add(new StackMachineBindingDeclaration { logicalName = texture.logicalName, declaredKind = StackMachineBindingKind.Resource });
            }
            if (ContainsBindingToken(block.TextureSource, "current"))
            {
                if (proxy == null)
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("material", "CurrentTextureProxyRequired", "A mask-only canvas recipe requires the co-located Material Proxy.", bindingName: block.BindingName);
                    return false;
                }
                if (!proxy.TryGetCurrentBaseColorTexture(block.BindingName, out Texture currentTexture, out MaterialProxyDiagnostic proxyDiagnostic))
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("material", "CurrentTextureUnavailable", proxyDiagnostic.message, bindingName: block.BindingName);
                    return false;
                }
                entries.Add(new TextureBindingEntry { logicalName = "current", kind = TextureBindingKind.SourceTexture, sourceTexture = currentTexture });
                document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "current", declaredKind = StackMachineBindingKind.Resource });
            }
            var candidate = new TextureRecipeStub(document, entries.ToArray());
            if (!TextureBindingContext.TryCreate(candidate, out _, out diagnostic)) return false;
            stub = candidate;
            return true;
        }

        private static bool ContainsBindingToken(string source, string logicalName)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(logicalName)) return false;
            string token = "$" + logicalName;
            int offset = 0;
            while ((offset = source.IndexOf(token, offset, System.StringComparison.Ordinal)) >= 0)
            {
                int end = offset + token.Length;
                if ((end == source.Length || char.IsWhiteSpace(source[end])) && (offset == 0 || char.IsWhiteSpace(source[offset - 1]))) return true;
                offset = end;
            }
            return false;
        }

        private static string FormatDiagnostic(StackMachineDiagnostic diagnostic)
        {
            return diagnostic == null ? "No structured diagnostic was returned." : diagnostic.domainCode + ": " + diagnostic.message;
        }
    }
}
