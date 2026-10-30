$ErrorActionPreference = 'Stop'
$revision = '10dbd2f06a8baf40ad285b037edda610c1b9a57c'
$modelId = 'sentence-transformers/all-MiniLM-L6-v2'
$destination = Join-Path $PSScriptRoot '../CorporateTaxCategorization.MachineLearning/Models/all-MiniLM-L6-v2'
$files = @('onnx/model.onnx', 'tokenizer.json', 'tokenizer_config.json', 'special_tokens_map.json', 'vocab.txt', 'config.json', 'sentence_bert_config.json', '1_Pooling/config.json', 'modules.json', 'README.md')
$entries = @()
foreach ($file in $files) {
    $target = Join-Path $destination $file
    New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
    $url = "https://huggingface.co/$modelId/resolve/$revision/$file"
    Invoke-WebRequest -Uri $url -OutFile "$target.download"
    $hash = (Get-FileHash -LiteralPath "$target.download" -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($file -eq 'onnx/model.onnx' -and $hash -ne '6fd5d72fe4589f189f8ebc006442dbb529bb7ce38f8082112682524616046452') {
        throw 'Downloaded ONNX model does not match the publisher SHA256.'
    }
    Move-Item -LiteralPath "$target.download" -Destination $target -Force
    $entries += [ordered]@{ path = $file; source = $url; sha256 = $hash; bytes = (Get-Item -LiteralPath $target).Length }
}
Invoke-WebRequest -Uri 'https://www.apache.org/licenses/LICENSE-2.0.txt' -OutFile (Join-Path $destination 'LICENSE.txt')
$entries += [ordered]@{ path = 'LICENSE.txt'; source = 'https://www.apache.org/licenses/LICENSE-2.0.txt'; sha256 = (Get-FileHash (Join-Path $destination 'LICENSE.txt') -Algorithm SHA256).Hash.ToLowerInvariant(); bytes = (Get-Item (Join-Path $destination 'LICENSE.txt')).Length }
[ordered]@{ modelId = $modelId; revision = $revision; license = 'Apache-2.0 (declared by upstream model card)'; purpose = 'English semantic similarity baseline; not a trained tax classifier'; files = $entries } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $destination 'manifest.json') -Encoding utf8
Write-Output "Model downloaded and ONNX SHA256 verified: $destination"
