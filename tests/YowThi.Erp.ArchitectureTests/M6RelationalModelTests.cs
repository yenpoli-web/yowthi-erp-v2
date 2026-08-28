using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using YowThi.Erp.Domain.Finance;
using YowThi.Erp.Infrastructure.Persistence;

namespace YowThi.Erp.ArchitectureTests;

public sealed class M6RelationalModelTests
{
    [Fact]
    public void M6_maps_exactly_finance_relations()
    {
        using var context = CreateContext();
        var model = GetDesignTimeModel(context);

        var expected = new[]
        {
            "finance.company_pickup_transport_bases",
            "finance.company_pickup_transport_obligation_basis_items",
            "finance.payable_adjustments",
            "finance.payable_obligation_items",
            "finance.payable_outstanding_positions",
            "finance.payables",
            "finance.payments",
            "finance.receivable_obligation_items",
            "finance.receivable_outstanding_positions",
            "finance.receivables",
            "finance.receipts",
        };

        var actual = model.GetEntityTypes()
            .Where(entityType => entityType.GetSchema() == "finance")
            .Select(entityType => $"{entityType.GetSchema()}.{entityType.GetTableName()}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected.OrderBy(value => value, StringComparer.Ordinal), actual);
    }

    [Fact]
    public void Payable_uses_confirmed_typed_shapes_and_partial_business_uniques()
    {
        using var context = CreateContext();
        var payable = GetTable(GetDesignTimeModel(context), "finance", "payables");

        Assert.Contains("ck_payables_kind", CheckNames(payable));
        Assert.Contains("ck_payables_typed_source", CheckNames(payable));
        Assert.Contains(payable.GetKeys(), key => PropertyNames(key.Properties).SequenceEqual(new[] { "Id", "PayableKind" }));
        Assert.Equal(typeof(PayableKind), payable.FindProperty("PayableKind")!.ClrType);

        var rowVersion = payable.FindProperty("RowVersion");
        Assert.NotNull(rowVersion);
        Assert.True(rowVersion!.IsConcurrencyToken);
        Assert.Equal(1L, rowVersion.GetDefaultValue());

        Assert.Contains(payable.GetIndexes(), index =>
            index.IsUnique &&
            PropertyNames(index.Properties).SequenceEqual(new[] { "ProcurementBatchId", "SupplierId" }) &&
            index.GetFilter() == "payable_kind = 'PROCUREMENT_SUPPLIER'");
        Assert.Contains(payable.GetIndexes(), index =>
            index.IsUnique &&
            PropertyNames(index.Properties).SequenceEqual(new[] { "ProcurementBatchId", "FarmerId" }) &&
            index.GetFilter() == "payable_kind = 'PROCUREMENT_FARMER'");
        Assert.Contains(payable.GetIndexes(), index =>
            index.IsUnique &&
            PropertyNames(index.Properties).SequenceEqual(new[] { "OutsourcedSupplyDetailId" }) &&
            index.GetFilter() == "payable_kind = 'OUTSOURCED_VENDOR'");
        Assert.Contains(payable.GetIndexes(), index =>
            index.IsUnique &&
            PropertyNames(index.Properties).SequenceEqual(new[] { "EmployeeDailyWageId" }) &&
            index.GetFilter() == "payable_kind = 'EMPLOYEE_DAILY_WAGE'");
        Assert.DoesNotContain(payable.GetIndexes(), index =>
            index.IsUnique && index.GetFilter()?.Contains("COMPANY_PICKUP_TRANSPORT", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Payable_obligation_is_flat_typed_lineage_with_kind_compatible_owner()
    {
        using var context = CreateContext();
        var obligation = GetTable(GetDesignTimeModel(context), "finance", "payable_obligation_items");

        var checks = CheckNames(obligation);
        Assert.Contains("ck_payable_obligation_items_kind", checks);
        Assert.Contains("ck_payable_obligation_items_amount", checks);
        Assert.Contains("ck_payable_obligation_items_typed_source", checks);
        Assert.Contains("ck_payable_obligation_items_transport_quantity", checks);
        Assert.Contains("ck_payable_obligation_items_transport_rate", checks);
        Assert.Equal(typeof(PayableKind), obligation.FindProperty("PayableKind")!.ClrType);
        Assert.Equal(typeof(PayableObligationKind), obligation.FindProperty("ObligationKind")!.ClrType);

        var payableForeignKey = Assert.Single(
            obligation.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.GetTableName() == "payables");
        Assert.Equal(new[] { "PayableId", "PayableKind" }, PropertyNames(payableForeignKey.Properties));
        Assert.Equal(new[] { "Id", "PayableKind" }, PropertyNames(payableForeignKey.PrincipalKey.Properties));
        Assert.Equal(DeleteBehavior.Restrict, payableForeignKey.DeleteBehavior);

        Assert.Contains(obligation.GetIndexes(), index => index.IsUnique && PropertyNames(index.Properties).SequenceEqual(new[] { "ProcurementEntryId" }));
        Assert.Contains(obligation.GetIndexes(), index => index.IsUnique && PropertyNames(index.Properties).SequenceEqual(new[] { "OutsourcedSupplyDetailId" }));
        Assert.Contains(obligation.GetIndexes(), index => index.IsUnique && PropertyNames(index.Properties).SequenceEqual(new[] { "EmployeeDailyWageId" }));
        Assert.Null(obligation.FindProperty("RowVersion"));
    }

    [Fact]
    public void Company_pickup_transport_preserves_per_entry_basis_and_many_to_many_lineage_without_grouping_rule()
    {
        using var context = CreateContext();
        var model = GetDesignTimeModel(context);
        var basis = GetTable(model, "finance", "company_pickup_transport_bases");
        var lineage = GetTable(model, "finance", "company_pickup_transport_obligation_basis_items");

        Assert.Contains("ck_company_pickup_transport_bases_quantity", CheckNames(basis));
        Assert.Contains(basis.GetIndexes(), index => index.IsUnique && PropertyNames(index.Properties).SequenceEqual(new[] { "ProcurementEntryId" }));
        Assert.Equal(new[] { "TransportObligationItemId", "TransportBasisId" }, PropertyNames(lineage.FindPrimaryKey()!.Properties));
        Assert.Contains("ck_company_pickup_transport_obligation_basis_items_quantity", CheckNames(lineage));
        Assert.Contains(lineage.GetForeignKeys(), foreignKey => foreignKey.PrincipalEntityType.GetTableName() == "payable_obligation_items" && foreignKey.DeleteBehavior == DeleteBehavior.Restrict);
        Assert.Contains(lineage.GetForeignKeys(), foreignKey => foreignKey.PrincipalEntityType.GetTableName() == "company_pickup_transport_bases" && foreignKey.DeleteBehavior == DeleteBehavior.Restrict);
    }

    [Fact]
    public void Payable_adjustment_and_payment_are_append_facts_with_confirmed_amount_signs()
    {
        using var context = CreateContext();
        var model = GetDesignTimeModel(context);
        var adjustment = GetTable(model, "finance", "payable_adjustments");
        var payment = GetTable(model, "finance", "payments");

        Assert.Contains("ck_payable_adjustments_type", CheckNames(adjustment));
        Assert.Contains("ck_payable_adjustments_amount_delta", CheckNames(adjustment));
        Assert.Equal(typeof(PayableAdjustmentType), adjustment.FindProperty("AdjustmentType")!.ClrType);
        Assert.Null(adjustment.FindProperty("RowVersion"));
        Assert.Contains(adjustment.GetForeignKeys(), foreignKey => foreignKey.PrincipalEntityType.GetSchema() == "system" && foreignKey.PrincipalEntityType.GetTableName() == "accounts" && foreignKey.DeleteBehavior == DeleteBehavior.Restrict);

        Assert.Contains("ck_payments_amount", CheckNames(payment));
        Assert.Null(payment.FindProperty("RowVersion"));
        Assert.Contains(payment.GetForeignKeys(), foreignKey => foreignKey.PrincipalEntityType.GetTableName() == "payables" && foreignKey.DeleteBehavior == DeleteBehavior.Restrict);
        Assert.Contains(payment.GetForeignKeys(), foreignKey => foreignKey.PrincipalEntityType.GetSchema() == "system" && foreignKey.PrincipalEntityType.GetTableName() == "accounts" && foreignKey.DeleteBehavior == DeleteBehavior.Restrict);
    }

    [Fact]
    public void Receivable_and_obligation_items_enforce_same_sale_lineage()
    {
        using var context = CreateContext();
        var model = GetDesignTimeModel(context);
        var receivable = GetTable(model, "finance", "receivables");
        var obligation = GetTable(model, "finance", "receivable_obligation_items");

        Assert.Contains(receivable.GetKeys(), key => PropertyNames(key.Properties).SequenceEqual(new[] { "Id", "SalesId" }));
        Assert.Contains(receivable.GetIndexes(), index => index.IsUnique && PropertyNames(index.Properties).SequenceEqual(new[] { "SalesId" }));
        Assert.True(receivable.FindProperty("RowVersion")!.IsConcurrencyToken);

        Assert.Contains("ck_receivable_obligation_items_amount", CheckNames(obligation));
        Assert.Contains(obligation.GetIndexes(), index => index.IsUnique && PropertyNames(index.Properties).SequenceEqual(new[] { "SalesDetailId" }));

        var receivableForeignKey = Assert.Single(
            obligation.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.GetTableName() == "receivables");
        Assert.Equal(new[] { "ReceivableId", "SalesId" }, PropertyNames(receivableForeignKey.Properties));
        Assert.Equal(new[] { "Id", "SalesId" }, PropertyNames(receivableForeignKey.PrincipalKey.Properties));

        var detailForeignKey = Assert.Single(
            obligation.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.GetTableName() == "sales_details");
        Assert.Equal(new[] { "SalesDetailId", "SalesId" }, PropertyNames(detailForeignKey.Properties));
        Assert.Equal(new[] { "Id", "SalesId" }, PropertyNames(detailForeignKey.PrincipalKey.Properties));
    }

    [Fact]
    public void Receipt_is_positive_append_fact()
    {
        using var context = CreateContext();
        var receipt = GetTable(GetDesignTimeModel(context), "finance", "receipts");

        Assert.Contains("ck_receipts_amount", CheckNames(receipt));
        Assert.Null(receipt.FindProperty("RowVersion"));
        Assert.Contains(receipt.GetForeignKeys(), foreignKey => foreignKey.PrincipalEntityType.GetTableName() == "receivables" && foreignKey.DeleteBehavior == DeleteBehavior.Restrict);
        Assert.Contains(receipt.GetForeignKeys(), foreignKey => foreignKey.PrincipalEntityType.GetSchema() == "system" && foreignKey.PrincipalEntityType.GetTableName() == "accounts" && foreignKey.DeleteBehavior == DeleteBehavior.Restrict);
    }

    [Fact]
    public void Outstanding_positions_are_formula_projections_and_monetary_concurrency_owners_without_nonnegative_outstanding_rule()
    {
        using var context = CreateContext();
        var model = GetDesignTimeModel(context);

        foreach (var table in new[] { "payable_outstanding_positions", "receivable_outstanding_positions" })
        {
            var position = GetTable(model, "finance", table);
            var checks = CheckNames(position);

            Assert.Equal(4, checks.Count);
            Assert.Contains($"ck_{table}_original", checks);
            Assert.Contains($"ck_{table}_settlement", checks);
            Assert.Contains($"ck_{table}_formula", checks);
            Assert.Contains($"ck_{table}_row_version", checks);

            var rowVersion = position.FindProperty("RowVersion");
            Assert.NotNull(rowVersion);
            Assert.True(rowVersion!.IsConcurrencyToken);
            Assert.Equal(1L, rowVersion.GetDefaultValue());
            Assert.Null(position.FindProperty("DeletedAt"));
        }
    }

    private static HashSet<string> CheckNames(IEntityType entityType)
    {
        return entityType.GetCheckConstraints().Select(check => check.Name!).ToHashSet(StringComparer.Ordinal);
    }

    private static string[] PropertyNames(IEnumerable<IReadOnlyProperty> properties)
    {
        return properties.Select(property => property.Name).ToArray();
    }

    private static ErpDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ErpDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=yowthi_erp_v2;Username=postgres",
                PostgreSqlProviderOptions.Configure)
            .Options;

        return new ErpDbContext(options);
    }

    private static IModel GetDesignTimeModel(ErpDbContext context)
    {
        return context.GetService<IDesignTimeModel>().Model;
    }

    private static IEntityType GetTable(IModel model, string schema, string table)
    {
        return model.GetEntityTypes().Single(entityType =>
            entityType.GetSchema() == schema && entityType.GetTableName() == table);
    }
}
