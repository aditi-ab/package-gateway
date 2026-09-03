if ([string]::IsNullOrWhiteSpace($env:PACKAGE_GATEWAY_TOKEN)) {
    throw "Set PACKAGE_GATEWAY_TOKEN to a short-lived test token before running this script."
}

$baseUrl = if ($env:PACKAGE_GATEWAY_URL) { $env:PACKAGE_GATEWAY_URL.TrimEnd('/') } else { 'http://localhost:8080' }

curl.exe --fail-with-body `
    --header "Authorization: Bearer $env:PACKAGE_GATEWAY_TOKEN" `
    "$baseUrl/nuget"
