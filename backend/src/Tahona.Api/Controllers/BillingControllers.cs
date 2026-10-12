using Microsoft.AspNetCore.Mvc;
using Tahona.Api.Infrastructure;
using Tahona.Application.Common;
using Tahona.Application.Features.Billing;
using Tahona.Domain.Authorization;

namespace Tahona.Api.Controllers;

[ApiController]
[Route("api/invoices")]
public class InvoicesController(InvoiceService invoices) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.BillingView)]
    public Task<PagedResult<InvoiceListItemDto>> List([FromQuery] InvoiceQuery query, CancellationToken ct) => invoices.ListAsync(query, ct);

    [HttpGet("{id:guid}"), HasPermission(Permissions.BillingView)]
    public Task<InvoiceDetailDto> Get(Guid id, CancellationToken ct) => invoices.GetAsync(id, ct);

    [HttpPost, HasPermission(Permissions.InvoicesManage)]
    public async Task<IActionResult> Create(CreateInvoiceRequest request, CancellationToken ct)
    {
        var id = await invoices.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id }, new { id });
    }

    [HttpPost("{id:guid}/issue"), HasPermission(Permissions.InvoicesManage)]
    public async Task<IActionResult> Issue(Guid id, CancellationToken ct)
    {
        await invoices.IssueAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/void"), HasPermission(Permissions.InvoicesManage)]
    public async Task<IActionResult> Void(Guid id, VoidInvoiceRequest request, CancellationToken ct)
    {
        await invoices.VoidAsync(id, request, ct);
        return NoContent();
    }

    [HttpGet("outstanding"), HasPermission(Permissions.BillingView)]
    public Task<IReadOnlyList<PatientBalanceDto>> Outstanding([FromQuery] int take = 100, CancellationToken ct = default) =>
        invoices.OutstandingBalancesAsync(take, ct);
}

[ApiController]
[Route("api/payments")]
public class PaymentsController(PaymentService payments) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.BillingView)]
    public Task<PagedResult<PaymentListItemDto>> List([FromQuery] PaymentQuery query, CancellationToken ct) => payments.ListAsync(query, ct);

    [HttpGet("summary"), HasPermission(Permissions.BillingView)]
    public Task<PaymentSummaryDto> Summary(CancellationToken ct) => payments.SummaryAsync(ct);

    [HttpPost, HasPermission(Permissions.PaymentsManage)]
    public Task<RecordPaymentResult> Record(RecordPaymentRequest request, CancellationToken ct) => payments.RecordAsync(request, ct);

    [HttpPost("{id:guid}/refund"), HasPermission(Permissions.PaymentsRefund)]
    public async Task<IActionResult> Refund(Guid id, RefundRequest request, CancellationToken ct) =>
        Ok(new { id = await payments.RefundAsync(id, request, ct) });
}

[ApiController]
[Route("api/expenses")]
public class ExpensesController(ExpenseService expenses) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.ExpensesView)]
    public Task<PagedResult<ExpenseDto>> List([FromQuery] ExpenseQuery query, CancellationToken ct) => expenses.ListAsync(query, ct);

    [HttpPost, HasPermission(Permissions.ExpensesManage)]
    public async Task<IActionResult> Create(ExpenseRequest request, CancellationToken ct) => Ok(new { id = await expenses.CreateAsync(request, ct) });

    [HttpPut("{id:guid}"), HasPermission(Permissions.ExpensesManage)]
    public async Task<IActionResult> Update(Guid id, ExpenseRequest request, CancellationToken ct)
    {
        await expenses.UpdateAsync(id, request, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}"), HasPermission(Permissions.ExpensesManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await expenses.DeleteAsync(id, ct);
        return NoContent();
    }
}
