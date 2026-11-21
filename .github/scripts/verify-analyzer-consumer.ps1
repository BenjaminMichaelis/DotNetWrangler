$project = Join-Path $PSScriptRoot "..\..\tests\AnalyzerConsumer\AnalyzerConsumer.csproj"
$output = & dotnet build $project --configuration Release --verbosity minimal -warnaserror 2>&1
$exitCode = $LASTEXITCODE
$log = $output -join [Environment]::NewLine
$log | Write-Output

if ($log -match "\b(AD0001|CS8032|CS9057)\b") {
    throw "The analyzer failed to load or threw an exception in the compiler host."
}

if ($exitCode -eq 0 -or $log -notmatch "(?m)error WRANGLER0001:") {
    throw "The compiler host did not report WRANGLER0001 from the analyzer project reference."
}
