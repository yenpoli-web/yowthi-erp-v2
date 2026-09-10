using System.Text.Json;
using YowThi.Erp.Api.Procurement;
using YowThi.Erp.Domain.Procurement;

namespace YowThi.Erp.Api.ContractTests;

public sealed class ProcurementRequestJsonContractTests
{
    [Theory]
    [InlineData("SUPPLIER", ProcurementSourceType.SUPPLIER)]
    [InlineData("FARMER", ProcurementSourceType.FARMER)]
    public void Confirm_procurement_entry_request_accepts_string_source_type(
        string sourceType,
        ProcurementSourceType expected)
    {
        var json = $$"""
            {
              "procurementDate": "2026-09-10",
              "procurementProductId": "01900000-0000-7000-8000-000000000001",
              "sourceType": "{{sourceType}}",
              "supplierId": null,
              "farmerId": null,
              "netQuantity": 1,
              "unitPrice": 1,
              "companyPickup": false,
              "receiptStorageLocationId": null
            }
            """;

        var request = JsonSerializer.Deserialize<ConfirmProcurementEntryRequest>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(request);
        Assert.Equal(expected, request.SourceType);
    }
}
