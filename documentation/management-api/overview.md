# GraphQL management API

The management endpoint is `POST /graphql`. It accepts JSON `{ "query": "...", "variables": { ... } }` and requires an authenticated local administration session or an Entra-compatible JWT, according to `Authentication__Mode`. Local session requests also require the antiforgery token managed by the administration UI. Production disables schema download and the interactive IDE. Collections that can grow use cursor connections with a default page size of 25 and maximum of 100.

```graphql
query Repositories($first: Int, $after: String) {
  repositories(first: $first, after: $after) {
    nodes { id name slug packageTypes enabled }
    pageInfo { hasNextPage endCursor }
    totalCount
  }
}
```

Filtering and sorting use explicit enum and input arguments. The endpoint enforces query-cost, batch, concurrency, rate, and authorization limits.

`packageTypes` reports the formats configured in a repository. The nullable singular `packageType` field remains for compatibility with repositories created through the earlier single-format contract. New integrations should use `packageTypes` and the format on each upstream.

Mutations return typed payloads. Expected failures appear in `errors` with stable codes: `VALIDATION`, `NOT_FOUND`, `CONFLICT`, or `INTERNAL`. Internal exceptions are not exposed.
