# Managed ZBIC Decompression Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add pure-C# decoding of Nintendo ZBIC frames without changing ordinary Zstandard behavior.

**Architecture:** Reuse the existing ZstdSharp 1.5.7 decoder. A `ZbicDecompressor` normalizes ZBIC frame magic in a private copy of the input and scopes a thread-local ZBIC entropy mode around decompression. In that mode the existing FSE call graph uses Nintendo's binary-interpolative normalized-count reader; outside it, the upstream reader is unchanged.

**Tech Stack:** C# 9, ZstdSharp unsafe decoder, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-29-zbic-support-design.md`

## Global Constraints

- Decompression only. Do not add ZBIC compression.
- No native code or external runtime dependency.
- Preserve standard Zstd decode behavior.
- Use ZBIC magic `0x4349425A` and the independently documented BIC normalized-count format.
- Keep ZBIC state thread-local and restore it in `finally`.

## Review Focus

- BIC low-probability mode must preserve `-1` normalized counts.
- Invalid/truncated BIC tables must return a Zstd error rather than read out of bounds.
- Multiple ZBIC frames must have every frame magic normalized before ordinary frame walking.
- Standard `Decompressor` must continue rejecting ZBIC magic.
- ZBIC mode must be restored after a decode failure.

---

### Task 1: ZBIC entropy reader

**Files:**
- Modify: `src/ZstdSharp/Unsafe/EntropyCommon.cs`
- Create: `src/ZstdSharp/Unsafe/Zbic.cs`

- [ ] Port the independently documented cumulative binary-interpolative FSE normalized-count reader.
- [ ] Route both `FSE_readNCount` and `FSE_readNCount_bmi2` through it only while thread-local ZBIC mode is active.
- [ ] Add frame-magic normalization helpers and scoped mode setter.

### Task 2: Managed public decoder

**Files:**
- Create: `src/ZstdSharp/ZbicDecompressor.cs`

- [ ] Add `GetDecompressedSize`, allocating `Unwrap`, and destination-buffer `Unwrap` APIs.
- [ ] Work on a private input copy, normalize all ZBIC frame magics, enter ZBIC entropy mode, decode with the existing DCtx, and restore mode in `finally`.

### Task 3: Independent vectors and integration tests

**Files:**
- Create: `src/ZstdSharp.Test/ZbicTest.cs`

- [ ] Test a hand-built valid ZBIC raw-block frame for complete frame plumbing.
- [ ] Test BIC normalized-count vectors derived from the independent `ruzstd-zbic` decoder, including normal and low-probability forms.
- [ ] Test malformed/truncated BIC data and verify normal `Decompressor` still rejects ZBIC magic.
- [ ] Run `dotnet test src/ZstdSharp.Test/ZstdSharp.Test.csproj -f net9.0` locally when a checkout is available.
