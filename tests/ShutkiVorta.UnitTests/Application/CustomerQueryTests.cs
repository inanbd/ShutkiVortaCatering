using System.Reflection;
using NSubstitute;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Customers;
using ShutkiVorta.Application.Features.Inquiries;
using ShutkiVorta.Application.Features.Orders;
using ShutkiVorta.Application.Features.Wholesale;
using ShutkiVorta.Domain.Inquiries;
using ShutkiVorta.UnitTests.TestDoubles;

namespace ShutkiVorta.UnitTests.Application;

/// <summary>What the customer pages send: who may ask, which customers have a page, and what each viewer is given.</summary>
public sealed class CustomerQueryTests
{
    private const string AccountId = "account-1";
    private const string Email = "ann.lee@example.test";

    private readonly ICustomerRepository _customers = Substitute.For<ICustomerRepository>();
    private readonly IStandingOrderRepository _standingOrders = Substitute.For<IStandingOrderRepository>();
    private readonly ICateringInquiryRepository _inquiries = Substitute.For<ICateringInquiryRepository>();
    private readonly FakeClock _clock = new(TestData.Now);

    public CustomerQueryTests()
    {
        _customers.GetAccountAsync(AccountId, Arg.Any<CancellationToken>())
            .Returns(new CustomerSummaryDto { AccountId = AccountId, Name = "Ann Lee", Email = "Ann.Lee@Example.test", AllOrderCount = 1, OrderCount = 1 });
        _customers.GetAccountOrdersAsync(AccountId, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([new OrderSummaryDto { OrderNumber = "SV-1" }]);
        _customers.GetGuestAsync(Email, Arg.Any<CancellationToken>())
            .Returns(new CustomerSummaryDto { Type = CustomerType.Guest, Name = "Ann L", Email = Email, AllOrderCount = 2, OrderCount = 2 });
        _customers.GetGuestOrdersAsync(Email, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new OrderSummaryDto { OrderNumber = "SV-2" }, new OrderSummaryDto { OrderNumber = "SV-3" }]);
        _standingOrders.GetForCustomerAsync(AccountId, Arg.Any<CancellationToken>()).Returns([new StandingOrderSummaryDto { Id = 7, BusinessName = "Ann's Cafe" }]);
        _inquiries.GetByEmailAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([CateringInquiry.Submit("Ann", Email, null, null, 40, "A wedding", TestData.Now)]);
    }

    [Fact]
    public void EveryCustomerRequest_NeedsCustomersView()
    {
        foreach (var type in new[] { typeof(GetCustomersQuery), typeof(GetCustomerQuery), typeof(GetGuestCustomerQuery) })
        {
            Assert.Equal(Permissions.CustomersView, type.GetCustomAttribute<RequirePermissionAttribute>()?.Permission);
            Assert.False(typeof(IRequireAdmin).IsAssignableFrom(type) || typeof(IRequireStaff).IsAssignableFrom(type));
        }
    }

    [Fact]
    public async Task TheList_IsAskedForWithCleanFilters()
    {
        CustomerSearch? asked = null;
        _customers.SearchAsync(Arg.Do<CustomerSearch>(s => asked = s), Arg.Any<CancellationToken>()).Returns(new CustomerListDto());

        await Handlers(FakeCurrentStaff.Admin()).Handle(
            new GetCustomersQuery { Search = "  ann  ", Type = (CustomerType)99, Sort = (CustomerSort)42, Page = 0, PageSize = 500 }, CancellationToken.None);

        Assert.Equal(new CustomerSearch { Search = "ann", Type = null, Sort = CustomerSort.LastOrder, Page = 1, PageSize = 100 }, asked);

        await Handlers(FakeCurrentStaff.Admin()).Handle(
            new GetCustomersQuery { Search = " ", Type = CustomerType.Restaurant, Sort = CustomerSort.Name, Page = 3 }, CancellationToken.None);
        Assert.Equal(new CustomerSearch { Type = CustomerType.Restaurant, Sort = CustomerSort.Name, Page = 3, PageSize = 25 }, asked);
    }

    [Fact]
    public async Task AnAccountsPage_HasItsOrdersAndStandingOrders_AndInquiriesOnlyForThoseWhoMaySeeThem()
    {
        var withoutInquiries = await Handlers(FakeCurrentStaff.With(StaffRole.Manager, Permissions.CustomersView))
            .Handle(new GetCustomerQuery(AccountId), CancellationToken.None);

        Assert.NotNull(withoutInquiries);
        Assert.Equal("SV-1", Assert.Single(withoutInquiries.Orders).OrderNumber);
        Assert.Equal("Ann's Cafe", Assert.Single(withoutInquiries.StandingOrders).BusinessName);
        Assert.Null(withoutInquiries.Inquiries);
        await _inquiries.DidNotReceiveWithAnyArgs().GetByEmailAsync(default!, default, default);

        var withInquiries = await Handlers(FakeCurrentStaff.With(StaffRole.Manager, Permissions.CustomersView, Permissions.InquiriesView))
            .Handle(new GetCustomerQuery($" {AccountId} "), CancellationToken.None);

        var inquiry = Assert.Single(withInquiries!.Inquiries!);
        Assert.Equal(("A wedding", 40), (inquiry.Message, inquiry.GuestCount));
        await _inquiries.Received(1).GetByEmailAsync("Ann.Lee@Example.test", CustomerQueryHandlers.InquiriesShown, Arg.Any<CancellationToken>());
        await _customers.Received().GetAccountOrdersAsync(AccountId, CustomerQueryHandlers.OrdersShown, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnAccountThatIsNotACustomer_HasNoPage()
    {
        var handlers = Handlers(FakeCurrentStaff.Admin());

        Assert.Null(await handlers.Handle(new GetCustomerQuery("staff-only"), CancellationToken.None));
        Assert.Null(await handlers.Handle(new GetCustomerQuery("  "), CancellationToken.None));
        await _customers.DidNotReceive().GetAccountOrdersAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AGuestsPage_IsFoundByOrderNumber_UnlessTheOrderBelongsToAnAccount()
    {
        _customers.GetOrderOwnerAsync("SV-2", Arg.Any<CancellationToken>()).Returns(new OrderOwnerDto { Email = Email });
        _customers.GetOrderOwnerAsync("SV-1", Arg.Any<CancellationToken>()).Returns(new OrderOwnerDto { Email = Email, AccountId = AccountId });
        var handlers = Handlers(FakeCurrentStaff.Admin());

        var guest = await handlers.Handle(new GetGuestCustomerQuery("SV-2"), CancellationToken.None);
        Assert.Null(guest!.AccountId);
        Assert.Equal("Ann L", guest.Details!.Customer.Name);
        Assert.Equal(["SV-2", "SV-3"], guest.Details.Orders.Select(o => o.OrderNumber));
        Assert.Empty(guest.Details.StandingOrders);
        Assert.Single(guest.Details.Inquiries!);

        // An order counted on an account names the account (the page sends the viewer there) and loads nothing else.
        var account = await handlers.Handle(new GetGuestCustomerQuery("SV-1"), CancellationToken.None);
        Assert.Equal((AccountId, (CustomerDetailsDto?)null), (account!.AccountId, account.Details));

        Assert.Null(await handlers.Handle(new GetGuestCustomerQuery("SV-404"), CancellationToken.None));
        Assert.Null(await handlers.Handle(new GetGuestCustomerQuery(""), CancellationToken.None));
        await _customers.Received(1).GetGuestAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Figures_LeaveCancelledOrdersOut_AndAverageTheRest()
    {
        var customer = new CustomerSummaryDto { AllOrderCount = 5, OrderCount = 3, OrderValue = 100m };
        Assert.Equal((2, 33.33m), (customer.CancelledCount, customer.AverageOrder));
        Assert.Equal(0m, new CustomerSummaryDto { AllOrderCount = 2 }.AverageOrder);
    }

    private CustomerQueryHandlers Handlers(FakeCurrentStaff staff) => new(_customers, _standingOrders, _inquiries, staff, _clock);
}
