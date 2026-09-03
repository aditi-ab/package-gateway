# npm clients

Repository `frontend` exposes registry URL:

```text
https://gateway.example/npm/frontend/
```

Configure a user or CI environment without committing the token:

```ini
registry=https://gateway.example/npm/frontend/
//gateway.example/npm/frontend/:_authToken=${PACKAGE_GATEWAY_TOKEN}
always-auth=true
```

`npm install`, `npm ci`, and `npm view` are supported, including scoped packages, dist-tags, exact version documents, transitive dependencies, tarball `GET`/`HEAD`, and integrity verification. Metadata tarball URLs are always rewritten to the gateway. Upstream `dist.integrity` is preserved because approved tarball bytes are byte-identical.

Standard gzip, deflate, and Brotli compression on upstream JSON metadata responses is supported.

Lifecycle scripts are inspected as text but never executed by the gateway. Their presence and suspicious command/network/environment signals feed configurable findings and risk scores.

The same `frontend` repository may also contain NuGet upstreams. npm requests use only enabled npm upstreams, ordered by their npm priority. Adding NuGet upstreams does not change this registry URL or npm resolution order.
