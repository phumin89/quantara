using System.Globalization;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TradingCompany.Domain;
using TradingCompany.Application;

[assembly: InternalsVisibleTo("TradingCompany.Domain.Tests")]
[assembly: InternalsVisibleTo("TradingCompany.LiveIntegration.Tests")]

namespace TradingCompany.Infrastructure;

public sealed record BrokerWalletBalance(string Currency, decimal Available, decimal Reserved, decimal Total);
// Raw market rules: quantity_step has no verified side/unit mapping yet.
public sealed record BrokerSymbolRules(string Symbol, DateTimeOffset ObservedAt, int BaseAssetScale, int QuoteAssetScale,
    int? BaseQuantityScale, int? QuoteQuantityScale, int PriceScale, decimal PriceStep, int QuantityScale,
    decimal QuantityStep, decimal MinimumQuoteSize, bool FreezeBuy, bool FreezeSell, bool FreezeCancel,
    string Status, string Source, string MarketSegment);
public sealed record BrokerOpenOrderEvidence(string Symbol, string OrderId, OrderSide Side, string Type,
    decimal Rate, decimal RawAmount, decimal RawReceive, decimal ReportedFee, decimal FeeCredit,
    string ParentOrderId, string SuperOrderId, string ClientId, long Timestamp);
// Raw evidence is deliberately not a BrokerFill: amount/fee normalization is a separate reconciliation gate.
public sealed record BrokerHistoryEntry(string Symbol, string TransactionId, string OrderId, string ParentOrderId,
    string SuperOrderId, string ClientId, string Side, string Type, decimal Rate, decimal RawAmount,
    decimal ReportedFee, decimal FeeCredit, long Timestamp);
public sealed record BrokerOrderFillEvidence(string OrderId, string TransactionId, decimal Rate, decimal RawAmount,
    decimal ReportedFee, decimal FeeCredit, long Timestamp);
public sealed record BrokerOrderEvidence(string Symbol, OrderSide Side, string OrderId, string FirstOrderId,
    string ParentOrderId, string LastOrderId, string ClientId, bool PostOnly, decimal RawAmount, decimal Rate,
    decimal ReportedFee, decimal FeeCredit, decimal RawFilled, decimal RawTotal, string Status,
    bool PartiallyFilled, decimal RawRemaining, IReadOnlyList<BrokerOrderFillEvidence> History);

/// <summary>Private broker transport. Execution requires the verified, owner-gated Live host.</summary>
public sealed class BitkubLiveBroker : IDisposable
{
    // The official permission table requires Trade for wstoken. Issue and discard a
    // token; this never creates/cancels an order or returns authentication material.
    public async Task<bool> CheckTradePermissionAsync(CancellationToken cancellationToken = default)
    {
        using var document = await SendSigned(HttpMethod.Post, "/api/v3/market/wstoken", "{}", cancellationToken);
        try
        {
            var root = document.RootElement;
            var code = Required(root, "error").GetInt32();
            if (code == 52 && !root.EnumerateObject().Any(p => p.NameEquals("result") && p.Value.ValueKind != JsonValueKind.Null))
                return false;
            if (code != 0) throw Unavailable();
            var token = Required(root, "result").GetString();
            if (token is null || token.Length is < 32 or > 2048 || token.Any(char.IsWhiteSpace) || token.Any(char.IsControl))
                throw Unavailable();
            return true;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or OverflowException)
        { throw Unavailable(); }
    }

    public async Task<LiveQuote> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var pair = Pair(symbol);
        var observedAt = clock.GetUtcNow();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.bitkub.com/api/v3/market/ticker?sym=" + pair);
        using var document = await Read(request, cancellationToken);
        try
        {
            var matches = document.RootElement.EnumerateArray().Where(item =>
                string.Equals(Required(item, "symbol").GetString(), pair, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1) throw Unavailable();
            var item = matches[0];
            var bid = Amount(item, "highest_bid");
            var ask = Amount(item, "lowest_ask");
            if (bid <= 0 || ask < bid) throw Unavailable();
            return new LiveQuote(symbol, bid, ask, observedAt);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or OverflowException)
        { throw Unavailable(); }
    }

    public async Task<BrokerSymbolRules> GetSymbolRulesAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var pair = Pair(symbol);
        var observedAt = clock.GetUtcNow();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.bitkub.com/api/v3/market/symbols");
        using var document = await Read(request, cancellationToken);
        try
        {
            var root = document.RootElement;
            if (Required(root, "error").GetInt32() != 0) throw Unavailable();
            var matches = Required(root, "result").EnumerateArray().Where(item =>
                string.Equals(Required(item, "symbol").GetString(), pair, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1) throw Unavailable();
            var item = matches[0];
            if (Required(item, "base_asset").GetString() != symbol[..^4] || Required(item, "quote_asset").GetString() != "THB")
                throw Unavailable();
            var result = new BrokerSymbolRules(symbol, observedAt, Scale(item, "base_asset_scale"), Scale(item, "quote_asset_scale"),
                OptionalScale(item, "base_quantity_scale"), OptionalScale(item, "quote_quantity_scale"),
                Scale(item, "price_scale"), Amount(item, "price_step"), Scale(item, "quantity_scale"),
                Amount(item, "quantity_step"), NumericAmount(item, "min_quote_size"),
                Required(item, "freeze_buy").GetBoolean(), Required(item, "freeze_sell").GetBoolean(),
                Required(item, "freeze_cancel").GetBoolean(), Identifier(item, "status"), Identifier(item, "source"),
                Identifier(item, "market_segment"));
            if (result.PriceStep <= 0 || result.QuantityStep <= 0 || result.MinimumQuoteSize <= 0) throw Unavailable();
            return result;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or OverflowException)
        { throw Unavailable(); }
    }

    // An empty list is only an open-order observation, never proof of cancellation or complete history.
    public async Task<IReadOnlyList<BrokerOpenOrderEvidence>> GetOpenOrdersAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var path = "/api/v3/market/my-open-orders?sym=" + Pair(symbol);
        using var document = await GetSignedRead(path, cancellationToken);
        try
        {
            var root = document.RootElement;
            if (Required(root, "error").GetInt32() != 0) throw Unavailable();
            var result = new List<BrokerOpenOrderEvidence>();
            var identities = new HashSet<(OrderSide, string)>();
            foreach (var item in Required(root, "result").EnumerateArray())
            {
                var side = Required(item, "side").GetString() switch
                {
                    "buy" => OrderSide.Buy,
                    "sell" => OrderSide.Sell,
                    _ => throw Unavailable()
                };
                var clientId = Required(item, "client_id").GetString();
                if (clientId is null || clientId.Length > 2048 || clientId.Any(char.IsControl)) throw Unavailable();
                var order = new BrokerOpenOrderEvidence(symbol, Identifier(item, "id"), side, Identifier(item, "type"),
                    Amount(item, "rate"), Amount(item, "amount"), Amount(item, "receive"), Amount(item, "fee"),
                    Amount(item, "credit"), Identifier(item, "parent_id"), Identifier(item, "super_id"), clientId,
                    Required(item, "ts").GetInt64());
                if (order.Type is not ("limit" or "market") || order.Rate <= 0 || order.RawAmount <= 0 ||
                    order.Timestamp <= 0 || !identities.Add((side, order.OrderId))) throw Unavailable();
                result.Add(order);
            }
            return result.ToArray();
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or OverflowException)
        { throw Unavailable(); }
    }

    private static int? OptionalScale(JsonElement item, string name)
        => item.EnumerateObject().Any(p => p.NameEquals(name)) ? Scale(item, name) : null;

    private static int Scale(JsonElement item, string name)
    {
        var value = Required(item, name).GetInt32();
        return value is >= 0 and <= 28 ? value : throw Unavailable();
    }
    // Wire encoding only: this is NOT risk approval or symbol-step normalization.
    // Keep internal until verified preparation and reconciliation implement ILiveBroker.
    internal static PreparedLiveOrder EncodeLimitRequest(LiveIntent intent)
    {
        // BuyBudget caps the cash debit including fees. Bitkub amt is a spending amount,
        // whose gross/net/credit relation is not verified; never silently drop this constraint.
        if (intent.BuyBudget is not null) throw new InvalidDataException("BROKER_BUDGET_ACCOUNTING_UNVERIFIED");
        if (intent.Mode != TradingMode.Live || !Enum.IsDefined(intent.Side) ||
            intent.AccountId.Value == Guid.Empty || intent.DecisionId == Guid.Empty || intent.Id == Guid.Empty ||
            intent.Quantity <= 0 || intent.LimitPrice <= 0 || intent.MandateVersion <= 0 || intent.SnapshotVersion <= 0 ||
            string.IsNullOrWhiteSpace(intent.EvidenceHash)) throw Unavailable();
        var amount = intent.Side == OrderSide.Buy ? intent.Quantity * intent.LimitPrice : intent.Quantity;
        return EncodeAmount(intent, amount);
    }

    // Caller is the verified execution adapter. Budget buys deliberately spend less than
    // the cap so that a fee charged outside amt cannot enlarge the approved cash debit.
    internal static PreparedLiveOrder EncodeBudgetLimitRequest(LiveIntent intent, decimal amount)
    {
        if (intent.BuyBudget is not { } budget || intent.Side != OrderSide.Buy || budget <= 0 ||
            budget != intent.Quantity * intent.LimitPrice || amount <= 0 || amount > budget)
            throw Unavailable();
        _ = EncodeLimitRequest(intent with { BuyBudget = null });
        return EncodeAmount(intent, amount);
    }

    private static PreparedLiveOrder EncodeAmount(LiveIntent intent, decimal amount)
    {
        var pair = Pair(intent.Symbol);
        if (amount <= 0) throw Unavailable();
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("sym", pair);
            writer.WritePropertyName("amt");
            writer.WriteRawValue(amount.ToString("0.############################", CultureInfo.InvariantCulture));
            writer.WritePropertyName("rat");
            writer.WriteRawValue(intent.LimitPrice.ToString("0.############################", CultureInfo.InvariantCulture));
            writer.WriteString("typ", "limit");
            writer.WriteString("client_id", intent.Id.ToString("N"));
            writer.WriteEndObject();
        }
        var bytes = buffer.ToArray();
        return new(intent, Encoding.UTF8.GetString(bytes), Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    internal async Task<BrokerSubmission> SendLimitRequestAsync(PreparedLiveOrder request, CancellationToken cancellationToken = default)
        => await SendLimitRequestAsync(request, null, cancellationToken);

    internal async Task<BrokerSubmission> SendBudgetLimitRequestAsync(PreparedLiveOrder request, decimal amount,
        CancellationToken cancellationToken = default)
        => await SendLimitRequestAsync(request, amount, cancellationToken);

    private async Task<BrokerSubmission> SendLimitRequestAsync(PreparedLiveOrder request, decimal? budgetAmount,
        CancellationToken cancellationToken)
    {
        try
        {
            var expected = budgetAmount is { } amount
                ? EncodeBudgetLimitRequest(request.Intent, amount) : EncodeLimitRequest(request.Intent);
            if (request.Body != expected.Body || request.BodyHash != expected.BodyHash)
                return new(BrokerSubmissionState.Rejected, null, "INVALID_PREPARED_REQUEST");
        }
        catch (Exception error) when (error is InvalidDataException or OverflowException or ArgumentException)
        { return new(BrokerSubmissionState.Rejected, null, "INVALID_PREPARED_REQUEST"); }

        try
        {
            var path = request.Intent.Side == OrderSide.Buy ? "/api/v3/market/place-bid" : "/api/v3/market/place-ask";
            using var response = await SendSigned(HttpMethod.Post, path, request.Body, cancellationToken);
            var root = response.RootElement;
            var code = Required(root, "error").GetInt32();
            if (code != 0)
            {
                // Contradictory acceptance evidence must never free a reservation.
                if (root.EnumerateObject().Any(p => p.NameEquals("result") && p.Value.ValueKind != JsonValueKind.Null))
                    throw Unavailable();
                var rejected = code is >= 1 and <= 8 or >= 11 and <= 15 or 17 or 18 or 22 or 25 or 30 or 52 or 55 or 56 or 57 or 61;
                return new(rejected ? BrokerSubmissionState.Rejected : BrokerSubmissionState.Unknown, null,
                    rejected ? "BROKER_REJECTED" : "BROKER_OUTCOME_UNKNOWN");
            }
            var receipt = Required(root, "result");
            var id = Identifier(receipt, "id");
            var amount = budgetAmount ?? (request.Intent.Side == OrderSide.Buy
                ? request.Intent.Quantity * request.Intent.LimitPrice : request.Intent.Quantity);
            if (Required(receipt, "typ").GetString() != "limit" || NumericAmount(receipt, "amt") != amount ||
                NumericAmount(receipt, "rat") != request.Intent.LimitPrice ||
                Required(receipt, "ci").GetString() != request.Intent.Id.ToString("N") ||
                !long.TryParse(Required(receipt, "ts").GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var timestamp) || timestamp <= 0)
                throw Unavailable();
            _ = NumericAmount(receipt, "fee");
            _ = NumericAmount(receipt, "cre");
            _ = NumericAmount(receipt, "rec");
            // Receipt fields are not fills, fee ledger entries or proof of reconciliation.
            return new(BrokerSubmissionState.Accepted, id, "BROKER_ACKNOWLEDGED");
        }
        catch (Exception error) when (error is InvalidDataException or JsonException or InvalidOperationException or
            FormatException or OverflowException or OperationCanceledException)
        { return new(BrokerSubmissionState.Unknown, null, "BROKER_OUTCOME_UNKNOWN"); }
    }

    private readonly HttpClient http;
    private readonly string apiKey;
    private readonly byte[] secret;
    private readonly TimeProvider clock;
    internal string CredentialFingerprint => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes(apiKey + "\0" + Encoding.UTF8.GetString(secret))));

    public BitkubLiveBroker(string apiKey, string secret)
        : this(apiKey, secret, new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false }, TimeProvider.System) { }

    internal BitkubLiveBroker(string apiKey, string secret, HttpMessageHandler handler, TimeProvider clock)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Any(char.IsControl) || string.IsNullOrWhiteSpace(secret))
            throw new ArgumentException("Broker credentials are missing or invalid.");
        this.apiKey = apiKey;
        this.secret = Encoding.UTF8.GetBytes(secret);
        this.clock = clock;
        http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 1024 * 1024 };
    }

    public async Task<IReadOnlyList<BrokerWalletBalance>> GetWalletAsync(CancellationToken cancellationToken = default)
    {
        using var document = await GetSignedRead("/api/v4/wallet/balances", cancellationToken);
        try
        {
            var root = document.RootElement;
            if (Required(root, "code").GetString() != "0") throw Unavailable();
            var balances = new List<BrokerWalletBalance>();
            foreach (var item in Required(root, "data").EnumerateArray())
            {
                var currency = Required(item, "currency").GetString();
                if (string.IsNullOrEmpty(currency) || currency.Length > 20 || currency.Any(c => !char.IsAsciiLetterUpper(c) && !char.IsAsciiDigit(c)))
                    throw Unavailable();
                var available = Amount(item, "available");
                var reserved = Amount(item, "reserved");
                var total = Amount(item, "total");
                if (available + reserved != total || balances.Any(b => b.Currency == currency)) throw Unavailable();
                balances.Add(new(currency, available, reserved, total));
            }
            if (!balances.Any(b => b.Currency == "THB")) throw Unavailable();
            return balances.ToArray();
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or OverflowException)
        { throw Unavailable(); }
    }

    public async Task<IReadOnlyList<BrokerHistoryEntry>> GetHistoryAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var pair = Pair(symbol);
        var entries = new Dictionary<string, BrokerHistoryEntry>(StringComparer.Ordinal);
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        try
        {
            for (var page = 0; page < 100; page++)
            {
                var path = "/api/v3/market/my-order-history?sym=" + pair + "&pagination_type=keyset&lmt=100";
                if (cursor is not null) path += "&cursor=" + Uri.EscapeDataString(cursor);
                using var document = await GetSignedRead(path, cancellationToken);
                var root = document.RootElement;
                if (Required(root, "error").GetInt32() != 0) throw Unavailable();
                foreach (var item in Required(root, "result").EnumerateArray())
                {
                    var entry = new BrokerHistoryEntry(symbol, Identifier(item, "txn_id"), Identifier(item, "order_id"),
                        Identifier(item, "parent_order_id"), Identifier(item, "super_order_id"), Required(item, "client_id").GetString() ?? "",
                        Identifier(item, "side"), Identifier(item, "type"), Amount(item, "rate"), Amount(item, "amount"),
                        Amount(item, "fee"), Amount(item, "credit"), Required(item, "ts").GetInt64());
                    if (entry.Side is not ("buy" or "sell") || entry.Type is not ("limit" or "market") ||
                        entry.Rate <= 0 || entry.RawAmount <= 0 || entry.Timestamp <= 0) throw Unavailable();
                    if (entries.TryGetValue(entry.TransactionId, out var previous) && previous != entry) throw Unavailable();
                    entries[entry.TransactionId] = entry;
                }
                var pagination = Required(root, "pagination");
                if (!Required(pagination, "has_next").GetBoolean()) return entries.Values.ToArray();
                cursor = Identifier(pagination, "cursor");
                if (!seenCursors.Add(cursor)) throw Unavailable();
            }
            throw Unavailable();
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or OverflowException)
        { throw Unavailable(); }
    }

    // Read-only evidence, not a terminal ledger transition. Even a "filled" status requires
    // separately verified fill units, fees and account reconciliation before releasing reserves.
    public async Task<BrokerOrderEvidence> GetOrderAsync(string symbol, string orderId, OrderSide side,
        CancellationToken cancellationToken = default)
    {
        var pair = Pair(symbol);
        if (!ValidIdentifier(orderId) || !Enum.IsDefined(side)) throw Unavailable();
        var path = "/api/v3/market/order-info?sym=" + pair + "&id=" + Uri.EscapeDataString(orderId) +
            "&sd=" + (side == OrderSide.Buy ? "buy" : "sell");
        using var document = await GetSignedRead(path, cancellationToken);
        try
        {
            if (Required(document.RootElement, "error").GetInt32() != 0) throw Unavailable();
            var item = Required(document.RootElement, "result");
            var clientId = Required(item, "client_id").GetString();
            if (clientId is null || clientId.Length > 2048 || clientId.Any(char.IsControl)) throw Unavailable();
            var history = new List<BrokerOrderFillEvidence>();
            var transactionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var fill in Required(item, "history").EnumerateArray())
            {
                var evidence = new BrokerOrderFillEvidence(Identifier(fill, "id"), Identifier(fill, "txn_id"),
                    NumericAmount(fill, "rate"), NumericAmount(fill, "amount"), NumericAmount(fill, "fee"),
                    NumericAmount(fill, "credit"), Required(fill, "timestamp").GetInt64());
                if (evidence.Rate <= 0 || evidence.RawAmount <= 0 || evidence.Timestamp <= 0 ||
                    !transactionIds.Add(evidence.TransactionId)) throw Unavailable();
                history.Add(evidence);
            }
            var order = new BrokerOrderEvidence(symbol, side, Identifier(item, "id"), Identifier(item, "first"),
                Identifier(item, "parent"), Identifier(item, "last"), clientId, Required(item, "post_only").GetBoolean(),
                Amount(item, "amount"), NumericAmount(item, "rate"), NumericAmount(item, "fee"), NumericAmount(item, "credit"),
                NumericAmount(item, "filled"), NumericAmount(item, "total"), Identifier(item, "status"),
                Required(item, "partial_filled").GetBoolean(), NumericAmount(item, "remaining"), history.ToArray());
            if (order.OrderId != orderId || order.RawAmount <= 0 || order.Rate <= 0 || order.RawTotal <= 0 ||
                order.Status is not ("filled" or "unfilled" or "cancelled") ||
                order.RawFilled > order.RawTotal || order.RawRemaining > order.RawTotal) throw Unavailable();
            return order;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or OverflowException)
        { throw Unavailable(); }
    }

    private static string Pair(string symbol)
        => symbol switch { "BTC-THB" => "btc_thb", "ETH-THB" => "eth_thb", "SOL-THB" => "sol_thb", _ => throw Unavailable() };

    public async Task<BrokerCancellation> CancelAsync(string symbol, string orderId, OrderSide side, CancellationToken cancellationToken = default)
    {
        var pair = Pair(symbol);
        if (!ValidIdentifier(orderId) || !Enum.IsDefined(side)) throw Unavailable();
        var body = JsonSerializer.Serialize(new { sym = pair, id = orderId, sd = side == OrderSide.Buy ? "buy" : "sell" });
        try
        {
            using var response = await SendSigned(HttpMethod.Post, "/api/v3/market/cancel-order", body, cancellationToken);
            var code = Required(response.RootElement, "error").GetInt32();
            return code switch
            {
                0 => new(BrokerSubmissionState.Accepted, "CANCEL_ACCEPTED"),
                // These reject the request, not the underlying order. Never release its funds here.
                >= 1 and <= 8 or 11 or 21 or 22 or 25 or 52 or 61 => new(BrokerSubmissionState.Rejected, "CANCEL_REJECTED"),
                _ => new(BrokerSubmissionState.Unknown, "CANCEL_OUTCOME_UNKNOWN")
            };
        }
        catch (Exception error) when (error is InvalidDataException or JsonException or InvalidOperationException or
            FormatException or OverflowException or OperationCanceledException)
        { return new(BrokerSubmissionState.Unknown, "CANCEL_OUTCOME_UNKNOWN"); }
    }

    private Task<JsonDocument> GetSignedRead(string path, CancellationToken cancellationToken)
        => SendSigned(HttpMethod.Get, path, null, cancellationToken);

    private async Task<JsonDocument> SendSigned(HttpMethod method, string path, string? body, CancellationToken cancellationToken)
    {
        using var timeRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.bitkub.com/api/v3/servertime");
        using var time = await Read(timeRequest, cancellationToken);
        if (time.RootElement.ValueKind != JsonValueKind.Number || !time.RootElement.TryGetInt64(out var timestamp) || timestamp <= 0 ||
            Math.Abs((decimal)clock.GetUtcNow().ToUnixTimeMilliseconds() - timestamp) > 30000)
            throw Unavailable();
        var timestampText = timestamp.ToString(CultureInfo.InvariantCulture);
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(timestampText + method.Method + path + body)));
        using var request = new HttpRequestMessage(method, "https://api.bitkub.com" + path);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("X-BTK-APIKEY", apiKey);
        request.Headers.Add("X-BTK-TIMESTAMP", timestampText);
        request.Headers.Add("X-BTK-SIGN", signature);
        return await Read(request, cancellationToken);
    }

    private async Task<JsonDocument> Read(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            // No retries and no response/error body in exceptions or logs.
            if (!response.IsSuccessStatusCode) throw Unavailable();
            return JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken));
        }
        catch (Exception error) when (error is HttpRequestException or JsonException ||
            (error is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        { throw Unavailable(); }
    }

    private static JsonElement Required(JsonElement item, string name)
    {
        var properties = item.EnumerateObject().Where(p => p.NameEquals(name)).ToArray();
        if (properties.Length != 1) throw Unavailable();
        return properties[0].Value;
    }

    private static decimal Amount(JsonElement item, string name)
    {
        var value = Required(item, name).GetString();
        if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) || amount < 0 ||
            NormalizeAmount(value!) != NormalizeAmount(amount.ToString(CultureInfo.InvariantCulture)))
            throw Unavailable();
        return amount;
    }

    private static string Identifier(JsonElement item, string name)
    {
        var value = Required(item, name).GetString();
        if (!ValidIdentifier(value)) throw Unavailable();
        return value!;
    }

    private static bool ValidIdentifier(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= 2048 && !value.Any(char.IsControl);

    private static decimal NumericAmount(JsonElement item, string name)
    {
        var value = Required(item, name);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var amount) || amount < 0 ||
            NormalizeAmount(value.GetRawText()) != NormalizeAmount(amount.ToString(CultureInfo.InvariantCulture))) throw Unavailable();
        return amount;
    }

    // decimal parsing can silently round. Compare exact significand/scale before accepting broker money.
    // Equivalent trailing zeros and scientific numeric notation do not change the value.
    private static (string Digits, int Scale) NormalizeAmount(string value)
    {
        var exponentAt = value.IndexOfAny(['e', 'E']);
        var mantissa = (exponentAt < 0 ? value : value[..exponentAt]).TrimStart('-');
        var point = mantissa.IndexOf('.');
        var digits = mantissa.Replace(".", "").TrimStart('0');
        if (digits.Length == 0) return ("0", 0);
        var exponent = exponentAt < 0 ? 0 : int.Parse(value[(exponentAt + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        var significant = digits.TrimEnd('0');
        var scale = checked((point < 0 ? 0 : mantissa.Length - point - 1) - exponent - (digits.Length - significant.Length));
        return (significant, scale);
    }

    private static InvalidDataException Unavailable() => new("BROKER_EVIDENCE_UNAVAILABLE");

    public void Dispose()
    {
        http.Dispose();
        CryptographicOperations.ZeroMemory(secret);
    }
}
