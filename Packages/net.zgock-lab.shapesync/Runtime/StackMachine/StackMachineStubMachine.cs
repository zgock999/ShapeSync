// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>
    /// Minimal pure-word interpreter used to verify the common Forth subset foundation.
    /// It deliberately does not resolve bindings or provide domain words.
    /// </summary>
    public sealed class StackMachineStubMachine
    {
        private readonly List<StackMachineValue> stack = new List<StackMachineValue>();
        private readonly Stack<StackMachineCallFrame> returnStack = new Stack<StackMachineCallFrame>();
        private readonly StackMachineWordSetBase wordSet;
        /// <summary>Creates a stub interpreter with an optional explicitly supplied word evaluator.</summary>
        /// <param name="wordSet">Domain word evaluator; when omitted, the no-op Spec12 stub word set is used.</param>
        public StackMachineStubMachine(StackMachineWordSetBase wordSet = null) { this.wordSet = wordSet ?? new StackMachineStubWordSet(); }
        /// <summary>Gets the current mutable interpreter data stack as a read-only view.</summary>
        public IReadOnlyList<StackMachineValue> DataStack => stack;
        /// <summary>Executes a common plan with built-in words, internal <c>CALL</c>/<c>EXIT</c>, and the supplied word set.</summary>
        /// <param name="plan">Validated immutable common plan to execute.</param>
        /// <returns>A common lifecycle result with a data-stack snapshot.</returns>
        public StackMachineExecutionResult Execute(StackMachinePlan plan)
        {
            var r = new StackMachineExecutionResult
            {
                Stage = StackMachineTransactionStage.Started,
                InstructionPointer = 0,
                DataStack = stack,
                ReturnDepth = 0
            };
            r.Lifecycle.Add(StackMachineTransactionStage.Started);
            stack.Clear();
            returnStack.Clear();
            if (plan == null)
            {
                return Fail(r, StackMachineDiagnostic.Create(StackMachineDiagnosticCode.MalformedDocument, "Plan is null."));
            }

            for (var ip = 0; ip < plan.Instructions.Count; ip++)
            {
                r.InstructionPointer = ip;
                var instruction = plan.Instructions[ip];
                if (instruction.Op == StackMachineOp.Call)
                {
                    if (!TryCall(instruction, plan, r, ref ip)) return r;
                    continue;
                }

                if (instruction.Op == StackMachineOp.Exit)
                {
                    if (TryExit(r, ref ip)) break;
                    continue;
                }

                if (!TryExecuteInstruction(instruction, r)) return r;
            }

            r.Stage = StackMachineTransactionStage.Succeeded;
            r.Lifecycle.Add(StackMachineTransactionStage.Succeeded);
            SnapshotDataStack(r);
            return r;
        }

        private bool TryExecuteInstruction(StackMachineInstruction instruction, StackMachineExecutionResult result)
        {
            switch (instruction.Op)
            {
                case StackMachineOp.PushNumber:
                    stack.Add(StackMachineValue.FromNumber(instruction.Number));
                    return true;
                case StackMachineOp.PushBoolean:
                    stack.Add(StackMachineValue.FromBoolean(instruction.Boolean));
                    return true;
                case StackMachineOp.Depth:
                    stack.Add(StackMachineValue.FromNumber(stack.Count));
                    return true;
                case StackMachineOp.Drop:
                case StackMachineOp.Dup:
                case StackMachineOp.Swap:
                case StackMachineOp.Over:
                case StackMachineOp.Rot:
                    return TryExecuteStackOperation(instruction.Op, result);
                case StackMachineOp.Negate:
                case StackMachineOp.Abs:
                case StackMachineOp.Increment:
                case StackMachineOp.Decrement:
                case StackMachineOp.Double:
                case StackMachineOp.Halve:
                case StackMachineOp.ZeroEqual:
                case StackMachineOp.ZeroLess:
                    return TryExecuteNumericUnary(instruction.Op, result);
                case StackMachineOp.Equal:
                    return TryExecuteEqual(result);
                case StackMachineOp.Add:
                case StackMachineOp.Subtract:
                case StackMachineOp.Multiply:
                case StackMachineOp.Divide:
                case StackMachineOp.Min:
                case StackMachineOp.Max:
                case StackMachineOp.Less:
                case StackMachineOp.Greater:
                    return TryExecuteNumericBinary(instruction.Op, result);
                case StackMachineOp.And:
                case StackMachineOp.Or:
                case StackMachineOp.Xor:
                case StackMachineOp.Not:
                    return TryExecuteBoolean(instruction.Op, result);
                case StackMachineOp.PushBinding:
                    Fail(result, StackMachineDiagnostic.Create(StackMachineDiagnosticCode.InvalidBinding, "Stub machine does not resolve domain bindings."));
                    return false;
                case StackMachineOp.DomainWord:
                    return TryExecuteDomainWord(instruction, result);
                default:
                    Fail(result, StackMachineDiagnostic.Create(StackMachineDiagnosticCode.UnknownToken, "Stub word is not implemented: " + instruction.Op));
                    return false;
            }
        }

        private bool TryExecuteStackOperation(StackMachineOp op, StackMachineExecutionResult result)
        {
            switch (op)
            {
                case StackMachineOp.Drop:
                    if (!Need(1, result)) return false;
                    stack.RemoveAt(stack.Count - 1);
                    return true;
                case StackMachineOp.Dup:
                    if (!Need(1, result)) return false;
                    stack.Add(stack[stack.Count - 1]);
                    return true;
                case StackMachineOp.Swap:
                    if (!Need(2, result)) return false;
                    var swapIndex = stack.Count - 1;
                    var top = stack[swapIndex];
                    stack[swapIndex] = stack[swapIndex - 1];
                    stack[swapIndex - 1] = top;
                    return true;
                case StackMachineOp.Over:
                    if (!Need(2, result)) return false;
                    stack.Add(stack[stack.Count - 2]);
                    return true;
                case StackMachineOp.Rot:
                    if (!Need(3, result)) return false;
                    var rotIndex = stack.Count - 3;
                    var first = stack[rotIndex];
                    stack[rotIndex] = stack[rotIndex + 1];
                    stack[rotIndex + 1] = stack[rotIndex + 2];
                    stack[rotIndex + 2] = first;
                    return true;
                default:
                    throw new InvalidOperationException("Expected stack operation.");
            }
        }

        private bool TryExecuteNumericUnary(StackMachineOp op, StackMachineExecutionResult result)
        {
            if (!PopNumber(result, out var value)) return false;
            if (op == StackMachineOp.ZeroEqual)
            {
                stack.Add(StackMachineValue.FromBoolean(value == 0));
                return true;
            }

            if (op == StackMachineOp.ZeroLess)
            {
                stack.Add(StackMachineValue.FromBoolean(value < 0));
                return true;
            }

            var output = op == StackMachineOp.Negate ? -value :
                op == StackMachineOp.Abs ? Math.Abs(value) :
                op == StackMachineOp.Increment ? value + 1 :
                op == StackMachineOp.Decrement ? value - 1 :
                op == StackMachineOp.Double ? value * 2 : value / 2;
            PushNumber(output, result);
            return result.Stage != StackMachineTransactionStage.Failed;
        }

        private bool TryExecuteEqual(StackMachineExecutionResult result)
        {
            if (!Need(2, result)) return false;
            var index = stack.Count - 1;
            var right = stack[index];
            var left = stack[index - 1];
            if (left.Tag != right.Tag || (left.Tag != StackMachineValueTag.Number && left.Tag != StackMachineValueTag.Boolean))
            {
                Fail(result, StackMachineDiagnostic.Create(StackMachineDiagnosticCode.TypeMismatch, "= requires two Numbers or two Booleans."));
                return false;
            }

            stack.RemoveRange(index - 1, 2);
            stack.Add(StackMachineValue.FromBoolean(left.Tag == StackMachineValueTag.Number ? left.Number == right.Number : left.Boolean == right.Boolean));
            return true;
        }

        private bool TryExecuteNumericBinary(StackMachineOp op, StackMachineExecutionResult result)
        {
            if (!PopNumber(result, out var right) || !PopNumber(result, out var left)) return false;
            if (op == StackMachineOp.Divide && right == 0)
            {
                Fail(result, StackMachineDiagnostic.Create(StackMachineDiagnosticCode.DivideByZero, "Division by zero."));
                return false;
            }

            if (op == StackMachineOp.Less)
            {
                stack.Add(StackMachineValue.FromBoolean(left < right));
                return true;
            }

            if (op == StackMachineOp.Greater)
            {
                stack.Add(StackMachineValue.FromBoolean(left > right));
                return true;
            }

            var output = op == StackMachineOp.Add ? left + right :
                op == StackMachineOp.Subtract ? left - right :
                op == StackMachineOp.Multiply ? left * right :
                op == StackMachineOp.Divide ? left / right :
                op == StackMachineOp.Min ? Math.Min(left, right) : Math.Max(left, right);
            PushNumber(output, result);
            return result.Stage != StackMachineTransactionStage.Failed;
        }

        private bool TryExecuteBoolean(StackMachineOp op, StackMachineExecutionResult result)
        {
            if (op == StackMachineOp.Not)
            {
                if (!PopBoolean(result, out var value)) return false;
                stack.Add(StackMachineValue.FromBoolean(!value));
                return true;
            }

            if (!PopBoolean(result, out var right) || !PopBoolean(result, out var left)) return false;
            stack.Add(StackMachineValue.FromBoolean(op == StackMachineOp.And ? left && right : op == StackMachineOp.Or ? left || right : left ^ right));
            return true;
        }

        private bool TryExecuteDomainWord(StackMachineInstruction instruction, StackMachineExecutionResult result)
        {
            if (wordSet.TryEvaluateWord(instruction, new StackMachineExecutionContext(stack), out var diagnostic)) return true;
            Fail(result, diagnostic ?? StackMachineDiagnostic.Create(StackMachineDiagnosticCode.UnknownToken, "Domain word was not accepted: " + instruction.WordId));
            return false;
        }
        private bool Need(int count, StackMachineExecutionResult r) { if(stack.Count>=count)return true; Fail(r,StackMachineDiagnostic.Create(StackMachineDiagnosticCode.StackUnderflow,"Data stack underflow."));return false; }
        private bool TryCall(StackMachineInstruction instruction, StackMachinePlan plan, StackMachineExecutionResult result, ref int ip)
        {
            if (instruction.Target < 0 || instruction.Target >= plan.Instructions.Count) { Fail(result, StackMachineDiagnostic.Create(StackMachineDiagnosticCode.InvalidControlFlow, "CALL target is invalid.")); return false; }
            returnStack.Push(new StackMachineCallFrame(ip + 1)); result.ReturnDepth = returnStack.Count; ip = instruction.Target - 1; return true;
        }
        private bool TryExit(StackMachineExecutionResult result, ref int ip)
        {
            if (returnStack.Count == 0) { result.Stage = StackMachineTransactionStage.Succeeded; result.ReturnDepth = 0; return true; }
            ip = returnStack.Pop().ReturnInstructionPointer - 1; result.ReturnDepth = returnStack.Count; return false;
        }
        private bool PopNumber(StackMachineExecutionResult r,out double value) { value=0;if(!Need(1,r))return false;var v=stack[stack.Count-1];if(v.Tag!=StackMachineValueTag.Number){Fail(r,StackMachineDiagnostic.Create(StackMachineDiagnosticCode.TypeMismatch,"Number required."));return false;}stack.RemoveAt(stack.Count-1);value=v.Number;return true; }
        private bool PopBoolean(StackMachineExecutionResult r,out bool value) { value=false;if(!Need(1,r))return false;var v=stack[stack.Count-1];if(v.Tag!=StackMachineValueTag.Boolean){Fail(r,StackMachineDiagnostic.Create(StackMachineDiagnosticCode.TypeMismatch,"Boolean required."));return false;}stack.RemoveAt(stack.Count-1);value=v.Boolean;return true; }
        private void PushNumber(double value,StackMachineExecutionResult r) { if(double.IsNaN(value)||double.IsInfinity(value)) Fail(r,StackMachineDiagnostic.Create(StackMachineDiagnosticCode.NonFiniteNumber,"Non-finite numeric result.")); else stack.Add(StackMachineValue.FromNumber(value)); }
        private void SnapshotDataStack(StackMachineExecutionResult result) { result.DataStack = Array.AsReadOnly(stack.ToArray()); }
        private StackMachineExecutionResult Fail(StackMachineExecutionResult r, StackMachineDiagnostic d) { r.Stage=StackMachineTransactionStage.Failed;r.Diagnostic=d;r.Lifecycle.Add(StackMachineTransactionStage.Failed);SnapshotDataStack(r);return r; }
    }
}
