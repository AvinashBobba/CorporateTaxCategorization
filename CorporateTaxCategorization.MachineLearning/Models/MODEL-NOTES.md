# Downloaded embedding baseline

Model: [sentence-transformers/all-MiniLM-L6-v2](https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2).
Pinned revision: `10dbd2f06a8baf40ad285b037edda610c1b9a57c`.
The publisher's standard FP32 ONNX export is stored at `all-MiniLM-L6-v2/onnx/model.onnx` (90,405,214 bytes).
Model SHA256: `6fd5d72fe4589f189f8ebc006442dbb529bb7ce38f8082112682524616046452`.

This is a compact English sentence-similarity baseline for retrieving reviewed ledger descriptions. It is not trained to assign our capital/revenue, tangible asset or P&L categories. No model has been established as best for this project: selection requires a representative accountant-reviewed evaluation set. Related financial sentiment models and tax disclosure classifiers target different labels; a finance/tax name alone does not establish suitability.

## Included assets and provenance

The model folder contains the ONNX graph, tokenizer JSON, WordPiece vocabulary, special token settings, architecture config, sentence length/pooling settings, upstream model card and Apache-2.0 licence text. `manifest.json` records the pinned source URLs, sizes and SHA256 hashes. The upstream model card declares Apache-2.0; preserve that card and licence when redistributing. These assets are copied into build/publish output under `Models/all-MiniLM-L6-v2`.

Re-download from the repository root:

```powershell
./scripts/Download-EmbeddingModel.ps1
```

The script verifies the ONNX bytes against the publisher's LFS SHA256 and records hashes for all companion files. The binary is currently stored directly in the workspace. If this repository is placed under source control, choose Git LFS or a versioned model artifact store for the approximately 90 MB file.

## Verified runtime contract

The downloaded graph was loaded successfully with this project's ONNX Runtime CPU package and executed with CLS/SEP special tokens as a structural smoke test. Inputs:

| Name | Type | Shape |
| --- | --- | --- |
| input_ids | int64 | [batch, sequence] |
| attention_mask | int64 | [batch, sequence] |
| token_type_ids | int64 | [batch, sequence] |

Output: `last_hidden_state`, float32 `[batch, sequence, 384]`. Smoke output was `[1,2,384]`, all 768 values finite. This establishes runtime load/execution, not text embedding parity or categorization accuracy.

## Text integration

The existing `OnnxEmbeddingSession` accepts float features and **cannot run this graph**. It remains a separate contract. MiniLmTextEncoder now supplies the text path used by CorporateTaxCategorization.Api:

1. Tokenize using the supplied tokenizer settings/vocabulary, including special tokens, truncation and attention mask. Use the sentence-transformer limit of 256 tokens.
2. Run these three int64 tensors through a reused inference session.
3. Apply attention-mask-aware mean pooling across token outputs, then L2-normalize to a 384-value sentence embedding. Do not flatten token vectors or use the CLS vector as a substitute.
4. Check embedding parity against the upstream Sentence Transformers implementation on fixed text examples.
5. Generate embeddings for both the incoming description and authorized reviewed precedents with this exact model/preprocessing version, then call `ReviewedExampleMatcher.Find`.

The REST API now runs local text inference and retrieves synthetic examples. Manual API and browser runs verify real-model inference and the workbook batch workflow. Upstream parity remains outstanding. Automatic tax decisions are not enabled. No test projects are included.
