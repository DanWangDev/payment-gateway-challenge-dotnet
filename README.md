# Payment Gateway

An API that lets a merchant take a card payment. It validates the request, asks a simulated acquiring
bank to authorize or decline it, and stores the outcome so it can be retrieved later for reconciliation.

Three outcomes are possible:

| Outcome | Meaning |
|---|---|
| `Authorized` | the bank approved the payment |
| `Declined` | the bank refused it |
| `Rejected` | the request was invalid, so it was refused **without contacting the bank** |

## Running it

### With Docker Compose

Needs Docker with Linux containers and Docker Compose; no local .NET SDK is required.

```bash
docker compose up --build -d
docker compose logs -f gateway
```

The gateway is available at `http://localhost:8081`. The simulator still uses host port `8080`;
inside Compose, the gateway calls `http://bank_simulator:8080` using the service name. Both containers
use port `8080` internally, but their host ports differ. Startup ordering is configured with
`depends_on`; it does not guarantee the simulator is ready to accept requests immediately.

For example:

```bash
curl -i -X POST http://localhost:8081/api/Payments \
  -H "Content-Type: application/json" \
  -d '{"cardNumber":"1234567890123451","expiryMonth":12,"expiryYear":2099,"currency":"GBP","amount":1050,"cvv":"123"}'
```

Follow the returned `Location` to retrieve the payment. The examples below also work against the
container by replacing `https://localhost:7092` with `http://localhost:8081`.

The Dockerfile publishes only the API using the .NET 8 SDK, then copies the output into an ASP.NET
runtime image and runs as its non-root `app` user. Source files and the SDK stay out of the final image.
The container runs in Production, so Swagger UI is disabled. This local demo binds the gateway to
loopback over HTTP; it does not configure certificates. The existing HTTPS redirection middleware
logs a warning on the first HTTP request because no HTTPS port is configured, and serves the request
over HTTP. A production host would need TLS termination and the corresponding proxy configuration.

To stop and remove this stack:

```bash
docker compose down
```

Payments are held in process memory and disappear whenever the gateway restarts.

### With the .NET SDK

Needs the .NET 8 SDK, and Docker for the bank simulator.

```bash
docker compose up -d bank_simulator           # start only the bank simulator (port 8080)
dotnet run --project src/PaymentGateway.Api    # start the gateway (https://localhost:7092)
```

Leave the gateway running and use another terminal for the HTTP examples or tests. The test suite does
not require Docker, the simulator, or a running gateway:

```bash
dotnet test
```

Swagger UI is at `https://localhost:7092/swagger` in Development.

The gateway reads the bank's address from `BankSimulator:BaseUrl` in `appsettings.json`, and refuses to
start if it is missing or is not an absolute HTTP(S) URL. To override it, set the
`BankSimulator__BaseUrl` environment variable before starting the gateway. For example, in Bash:

```bash
BankSimulator__BaseUrl=http://localhost:8080 dotnet run --project src/PaymentGateway.Api
```

Or in PowerShell:

```powershell
$env:BankSimulator__BaseUrl = 'http://localhost:8080'
dotnet run --project src/PaymentGateway.Api
```

### Trying it by hand

```bash
curl -ki -X POST https://localhost:7092/api/Payments \
  -H "Content-Type: application/json" \
  -d '{"cardNumber":"1234567890123451","expiryMonth":12,"expiryYear":2099,"currency":"GBP","amount":1050,"cvv":"123"}'
```

The response includes a `Location` header pointing to the new payment. Request that URL to retrieve it,
or replace `<payment-id>` below with the `id` from the response body:

```bash
curl -ki "https://localhost:7092/api/Payments/<payment-id>"
```

An invalid card number exercises rejection without contacting the bank:

```bash
curl -ki -X POST https://localhost:7092/api/Payments \
  -H "Content-Type: application/json" \
  -d '{"cardNumber":"1234","expiryMonth":12,"expiryYear":2099,"currency":"GBP","amount":1050,"cvv":"123"}'
```

This returns `400 Bad Request` with:

```json
{"status":"Rejected","errors":["Card number must be 14 to 19 digits."]}
```

The simulator decides from the **last digit** of the card number, which is what makes the three paths
easy to exercise:

| Card ends in | Simulator | Gateway |
|---|---|---|
| 1, 3, 5, 7, 9 | `200` authorized | `201` with `Authorized` |
| 2, 4, 6, 8 | `200` not authorized | `201` with `Declined` |
| 0 | `503` | `502` - the bank gave no answer |

## API

### `POST /api/Payments`

```json
{
  "cardNumber": "1234567890123456",
  "expiryMonth": 12,
  "expiryYear": 2099,
  "currency": "GBP",
  "amount": 1050,
  "cvv": "123"
}
```

| Field | Rules |
|---|---|
| `cardNumber` | required, 14-19 ASCII digits |
| `expiryMonth` | required, 1-12 |
| `expiryYear` | required; the month/year pair must not be in the past |
| `currency` | required, one of `GBP`, `USD`, `EUR`; trimmed and upper-cased before use |
| `amount` | required, positive integer in minor units (1050 is £10.50 for GBP) |
| `cvv` | required, 3-4 ASCII digits |

| Response | When | Body |
|---|---|---|
| `201 Created` | the bank answered - authorized **or** declined | the stored payment; `Location` points at the `GET` |
| `400 Bad Request` | the request was invalid | `{"status":"Rejected","errors":["..."]}` |
| `502 Bad Gateway` | the bank was unreachable, errored, timed out, or answered unreadably | ProblemDetails |

Nothing is stored on `400` or `502`, and the bank is never contacted on `400`.

### `GET /api/Payments/{id}`

`200` with the stored payment, or `404` when it is unknown.

```json
{
  "id": "af81ca5d-787d-4fc3-9b74-5a1f299ae2b5",
  "status": "Authorized",
  "cardNumberLastFour": "3451",
  "expiryMonth": 12,
  "expiryYear": 2099,
  "currency": "GBP",
  "amount": 1050
}
```

`status` is `Authorized` or `Declined`. `Rejected` only ever appears in the `400` body, because a
rejected payment is never stored. **Only the last four digits of the card number are ever returned**,
and the CVV is never stored or returned at all.

## Design decisions and assumptions

**Validation**

- **Three outcomes, and only one is ours.** `Authorized`/`Declined` are the bank's answers; `Rejected`
  is the gateway refusing a request without calling the bank.
- **Validation is a pure function** - `PaymentRequestValidator.Validate(request, DateOnly today)`.
  `today` is a parameter rather than a clock read, so the rules are deterministic and the tests pin a
  fixed date instead of racing midnight. The controller supplies the current UTC date.
- **A card is valid through the end of its expiry month**, so one expiring in the current month is still
  valid. Only the month/year pair is compared, and there is no upper bound.
- **Card number and CVV are strings, not numbers.** A PAN does not fit an integer, and a CVV like `"012"`
  would lose its leading zero.
- **No Luhn check.** The assignment requires numeric characters and a length check, but no checksum.
  This also keeps the supplied simulator test cards usable. Checksum and BIN validation are possible
  extensions for a real gateway.
- **Currency is an allowlist of three ISO 4217 codes** (the challenge's limit for this exercise). It is
  trimmed and upper-cased *before* validation, so the validated, stored and bank-submitted currency are
  the same value.
- **`amount` is a positive integer in minor units.** Zero and negative are rejected.

**Responses**

- **`201` for both `Authorized` and `Declined`** - both create a payment. `Location` comes from
  `CreatedAtAction`.
- **A bank failure returns `502` because the gateway has no reliable authorization decision.** A timeout
  may occur after the bank has authorized the payment, so retrying without idempotency can create a
  duplicate.
- **One rejection contract.** Requests the validator refuses, and requests the framework cannot bind at
  all (malformed JSON, ill-typed values, explicit nulls), both return
  `{"status":"Rejected","errors":[...]}`, so a merchant has one shape to handle. The framework's
  automatic validation still runs first, so the action never sees an unbound payload.
- **Declined payments are stored**, because reconciliation has to retrieve them.
- **`502` keeps the framework's ProblemDetails**, deliberately: an infrastructure failure and a business
  rejection are different things and should not share a body.

**Storage and structure**

- **In-memory store** (`ConcurrentDictionary`), as the challenge allows. The dictionary provides safe
  concurrent lookups and additions, keyed by payment id. Singleton registration shares the repository
  across requests within one gateway instance. Payments disappear on restart and are not shared between
  gateway instances. No retention or eviction policy is implemented.
- **No interface for `BankClient` and no service layer.** One concrete client with orchestration in the
  controller is the honest size for two endpoints.
- **GET is synchronous** because the repository performs an immediate dictionary lookup. POST awaits
  the bank request. A database implementation would introduce asynchronous repository reads and
  propagate cancellation through GET.
- **The bank's `authorization_code` is modelled but never stored or returned.** A real gateway would
  retain bank references to support reconciliation and investigation; the challenge's response fields
  do not include this code.
- **Merchant-facing copy lives in `Resources/`, not in `.resx`.** One locale, one audience. If
  localisation were ever needed, the right first move for a machine-consumed API is a stable `code` per
  error that merchants localise themselves - not server-side translation of prose.

## Logging and tracing

The gateway writes structured logs to the console:

- Payment outcomes include `PaymentId` and `Status` at Information level. Validation and request-binding
  rejections are also Information events; they are expected client errors.
- Bank decisions include `Status`, `StatusCode` and `ElapsedMs`. Bank failures are Warning events with
  a fixed `FailureReason` (`UnexpectedStatus`, `TransportError`, `Timeout`, `MissingDecision` or
  `UnreadableResponse`), elapsed time and an HTTP status when available. Transport failures also
  include the .NET `HttpRequestError` classification. Caller cancellation propagates without a
  bank-failure warning.
- Request/response bodies, full card numbers, CVVs and raw exception messages are not included in
  these application logs. Request-binding failures use a fixed description because framework error
  details can contain submitted values.

The simple console formatter includes the platform's logging scopes, including `TraceId` and `SpanId`.
Search for a `TraceId` to connect a bank event to the resulting payment or rejection, including requests
that fail before a payment ID exists. ASP.NET Core accepts W3C `traceparent` headers, and the normal
`HttpClient` pipeline propagates trace context to the bank. The bank must consume that context for its
own telemetry to correlate; propagation alone does not provide a tracing backend.

To check correlation manually, add this header to one of the POST examples above:

```bash
-H "traceparent: 00-0123456789abcdef0123456789abcdef-1111111111111111-01"
```

The gateway's application logs for that request should show
`TraceId:0123456789abcdef0123456789abcdef`. No custom correlation middleware is needed.

## Assumptions and out of scope

Recorded here so they read as decisions rather than oversights:

- **No idempotency.** A retried `POST` creates a second payment. A real gateway keys on an
  `Idempotency-Key` header and replays the original response.
- **No retries and no circuit breaker.** The client fails fast to `502` after a 10-second timeout.
  Retrying a timeout needs care - the bank may have authorized a payment the gateway never recorded.
- **No merchant authentication and no rate limiting.** Both are outside this exercise, and neither is
  optional in production.
- **No payment lifecycle.** `Authorized` is terminal here; real flows carry on to capture, void and
  refund, with webhooks and settlement files.
- **Amounts are passed through unchanged as integer minor units.** The maximum accepted amount is
  2,147,483,647 minor units (£21,474,836.47 for GBP). Larger values are rejected during request binding.
- **No metrics, trace exporter or centralized log storage.** Console logs and built-in trace context
  support local investigation. Production monitoring would add collection, dashboards and alerts.
- **Discarding bank references limits reconciliation and investigation.** The stored gateway payment
  does not retain the bank's authorization code.

## Tests

`dotnet test` runs the whole suite with no Docker and no simulator. The bank is replaced by a
`StubBankHandler` - an `HttpMessageHandler` that records the requests it receives, so the exact wire
format and the call counts can be asserted. Coverage covers validation rules and their boundaries, the
bank client's response mapping and failure modes (timeout, unreachable, error status, unreadable body,
caller cancellation), and both endpoints end to end through `WebApplicationFactory`.

Focused logging assertions share those behavior tests: outcome fields and levels, failure reasons,
nonnegative bank duration, and no bank-failure log for caller cancellation. A separate regression test
puts card details into an exception message and checks that neither rendered logs nor structured fields
expose them. Console trace correlation is checked manually as described above.

CI builds and tests every push and pull request, and publishes the coverage summary on the run.

## Project structure

```
src/PaymentGateway.Api
    Controllers/    GET and POST endpoints
    Models/         request and response DTOs
    Resources/      merchant-facing rejection copy
    Services/       validator, currency allowlist, bank client, in-memory repository
test/PaymentGateway.Api.Tests
imposters/          bank simulator configuration (provided, unchanged)
Dockerfile          builds and packages the API
.dockerignore       excludes local build output and unrelated files from the build context
docker-compose.yml  runs the gateway and bank simulator
```
