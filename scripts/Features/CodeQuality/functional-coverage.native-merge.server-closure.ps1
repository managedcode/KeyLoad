function Assert-FcNativeServerImageClosure([string] $ContextDirectory, [object] $Context,
    [object] $Manifest, [object] $Bounds, [string] $ExpectedRenderedDockerfileSha256, [object] $ContextFile) {
    Assert-FcNativeServerMaterializedFiles $ContextDirectory $Context $Bounds $ExpectedRenderedDockerfileSha256 $ContextFile
    Assert-FcNativeServerDependencyRoster $ContextDirectory $Context $Manifest $Bounds
}

function Assert-FcNativeServerMaterializedFiles([string] $ContextDirectory, [object] $Context,
    [object] $Bounds, [string] $ExpectedDockerfileSha256, [object] $ContextFile) {
    if (-not [IO.Path]::IsPathFullyQualified($ContextDirectory) -or
        $ContextDirectory.Length -gt $Bounds.maximumPathCharacters) { throw $script:FcNativeContext.Invalid }
    $directory = [IO.Path]::GetFullPath($ContextDirectory)
    Assert-FcNativeNoReparsePath $directory
    if (-not [IO.Directory]::Exists($directory)) { throw $script:FcNativeContext.Invalid }
    $dockerfilePath = Join-Path $directory 'Dockerfile'
    $dockerfile = Read-FcNativeBoundedFile $dockerfilePath $Bounds.maximumManifestBytes $Bounds.readBufferBytes $script:FcNativeContext.Invalid
    $dockerfileEntry = $Context.files | Where-Object path -ceq 'Dockerfile'
    if (@($dockerfileEntry).Count -ne 1 -or $dockerfile.sha256 -cne $dockerfileEntry.sha256 -or
        $dockerfile.sha256 -cne $ExpectedDockerfileSha256) { throw $script:FcNativeContext.Invalid }
    $manifestPath = Join-Path $directory 'context-manifest.json'
    $actualManifest = Read-FcNativeHashFile $manifestPath $Bounds.maximumManifestBytes $Bounds.readBufferBytes $script:FcNativeContext.Invalid
    if ($actualManifest.sha256 -cne $ContextFile.sha256 -or $actualManifest.length -ne $ContextFile.length) {
        throw $script:FcNativeContext.Invalid
    }
    $expectedDockerfile = Read-FcNativeRenderedDockerfile $Context.baseImage.reference $Bounds
    if (-not [Linq.Enumerable]::SequenceEqual([byte[]] $expectedDockerfile, [byte[]] $dockerfile.bytes)) {
        throw $script:FcNativeContext.Invalid
    }
}

function Read-FcNativeRenderedDockerfile([string] $BaseReference, [object] $Bounds) {
    $templatePath = Join-Path $Repository 'scripts/Features/CodeQuality/functional-coverage.server-image.dockerfile'
    $template = Read-FcNativeBoundedFile $templatePath $Bounds.maximumManifestBytes $Bounds.readBufferBytes $script:FcNativeContext.Invalid
    if ($template.sha256 -cne $script:FcNativeMergeInput.ScriptHashes['functional-coverage.server-image.dockerfile']) {
        throw $script:FcNativeContext.Invalid
    }
    $placeholder = '{{SOURCE_BOUND_ASPNET_IMAGE}}'
    $text = [Text.Encoding]::UTF8.GetString($template.bytes)
    if ([regex]::Matches($text, [regex]::Escape($placeholder)).Count -ne 1) { throw $script:FcNativeContext.Invalid }
    [Text.Encoding]::UTF8.GetBytes($text.Replace($placeholder, $BaseReference))
}

function Assert-FcNativeServerDependencyRoster([string] $ContextDirectory, [object] $Context,
    [object] $Manifest, [object] $Bounds) {
    $relativePath = 'server/KeyLoad.Server.deps.json'
    $metadata = @($Context.files | Where-Object path -ceq $relativePath)
    if ($metadata.Count -ne 1) { throw $script:FcNativeContext.Invalid }
    $depsPath = Join-Path $ContextDirectory 'server/KeyLoad.Server.deps.json'
    $deps = Read-FcNativeBoundedFile $depsPath $Bounds.maximumManifestBytes $Bounds.readBufferBytes $script:FcNativeContext.Invalid
    if ($deps.sha256 -cne $metadata[0].sha256 -or $deps.length -ne $metadata[0].length) { throw $script:FcNativeContext.Invalid }
    $document = [System.Text.Json.JsonDocument]::Parse([ReadOnlyMemory[byte]]::new($deps.bytes))
    try {
        Assert-FcNativeJsonUnique $document.RootElement
        $value = ConvertFrom-Json -InputObject ([Text.Encoding]::UTF8.GetString($deps.bytes)) -AsHashtable -Depth 16
    }
    finally { $document.Dispose() }
    $modules = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($library in $value.libraries.Keys) {
        if ([string] $library -match '\A(KeyLoad\.[A-Za-z0-9.]+)/[^/]+\z') { [void] $modules.Add($Matches[1]) }
    }
    if (-not $modules.SetEquals([string[]] $script:FcNativeMergeInput.ServerModuleRoster)) { throw $script:FcNativeContext.Invalid }
    $compiledModules = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($product in $Manifest.value.compiledProducts) {
        if ($product.module -cin $script:FcNativeMergeInput.ServerModuleRoster) { [void] $compiledModules.Add([string] $product.module) }
    }
    if (-not $compiledModules.SetEquals([string[]] $modules)) { throw $script:FcNativeContext.Invalid }
}
