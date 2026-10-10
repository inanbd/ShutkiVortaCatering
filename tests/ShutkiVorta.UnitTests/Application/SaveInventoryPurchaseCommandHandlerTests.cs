using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ShutkiVorta.Application.Common.Exceptions;
using ShutkiVorta.Application.Common.Files;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Features.Inventory;
using ShutkiVorta.Domain.Inventory;
using ShutkiVorta.UnitTests.TestDoubles;

namespace ShutkiVorta.UnitTests.Application;

public sealed class SaveInventoryPurchaseCommandHandlerTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 0x49, 0x48, 0x44, 0x52];
    private static readonly DateOnly Today = DateOnly.FromDateTime(TestData.Now);

    private readonly IInventoryRepository _repository = Substitute.For<IInventoryRepository>();
    private readonly IReceiptStorage _storage = Substitute.For<IReceiptStorage>();
    private readonly FakeCurrentUser _user = new() { UserId = "u1", Email = "admin@test.local", IsAdmin = true };

    public SaveInventoryPurchaseCommandHandlerTests()
    {
        var stored = 0;
        _storage.SaveAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => $"stored-{++stored}{call.ArgAt<string>(1)}");
    }

    private SaveInventoryPurchaseCommandHandler CreateHandler() =>
        new(_repository, _storage, new FakeClock(TestData.Now), _user, NullLogger<SaveInventoryPurchaseCommandHandler>.Instance);

    private static SaveInventoryPurchaseCommand Command(params ReceiptUpload[] receipts) => new()
    {
        PurchasedOn = Today,
        Store = "Desi grocery",
        Lines = [new("Mustard oil", 4m, "bottle", 27.96m), new("Garlic", 2m, "lb", 4.98m)],
        NewReceipts = receipts,
    };

    private static ReceiptUpload Upload(byte[] content, string name = "receipt.png") => new(new MemoryStream(content), name, content.Length);

    [Fact]
    public async Task NewPurchase_StoresTheReceipts_AndRecordsWhoAddedIt()
    {
        InventoryPurchase? saved = null;
        await _repository.AddPurchaseAsync(Arg.Do<InventoryPurchase>(p => saved = p), Arg.Any<CancellationToken>());

        await CreateHandler().Handle(Command(Upload(Png)), CancellationToken.None);

        Assert.NotNull(saved);
        Assert.Equal("admin@test.local", saved.CreatedBy);
        Assert.Equal(32.94m, saved.Total);
        var receipt = Assert.Single(saved.Receipts);
        Assert.Equal("stored-1.png", receipt.FileName);
        Assert.Equal("image/png", receipt.ContentType);
        Assert.Equal("receipt.png", receipt.OriginalFileName);
    }

    [Fact]
    public async Task AFileThatIsNotAPhoto_IsRejected_BeforeAnythingIsStored()
    {
        var text = "Total: $32.94 thank you"u8.ToArray();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            CreateHandler().Handle(Command(Upload(Png), Upload(text, "receipt.txt")), CancellationToken.None));

        Assert.Contains("\"receipt.txt\" is not a JPG, PNG or WebP photo", ex.Message);
        await _storage.DidNotReceiveWithAnyArgs().SaveAsync(default!, default!, default);
        await _repository.DidNotReceiveWithAnyArgs().AddPurchaseAsync(default!, default);
    }

    [Fact]
    public async Task APhotoLargerThan10MB_IsRejected()
    {
        var upload = new ReceiptUpload(new MemoryStream(Png), "huge.png", SaveInventoryPurchaseCommand.MaxReceiptBytes + 1);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => CreateHandler().Handle(Command(upload), CancellationToken.None));

        Assert.Contains("larger than 10 MB", ex.Message);
    }

    [Fact]
    public async Task WhenSavingFails_ThePhotosJustStoredAreDeleted()
    {
        _repository.AddPurchaseAsync(Arg.Any<InventoryPurchase>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("database down"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateHandler().Handle(Command(Upload(Png), Upload(Png)), CancellationToken.None));

        await _storage.Received(1).DeleteAsync("stored-1.png", Arg.Any<CancellationToken>());
        await _storage.Received(1).DeleteAsync("stored-2.png", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemovingAReceipt_DeletesItsFileOnlyAfterThePurchaseIsSaved()
    {
        var existing = InventoryPurchase.Record(Today, null, null, [new("Salt", 1m, "lb", 1m)], "admin@test.local", Today, TestData.Now);
        existing.AssignId(5);
        existing.AddReceipt("old.jpg", "old.jpg", ImageFormat.Jpeg.ContentType, 100, TestData.Now).AssignId(9);
        _repository.GetPurchaseAsync(5, Arg.Any<CancellationToken>()).Returns(existing);

        var id = await CreateHandler().Handle(Command() with { Id = 5, RemoveReceiptIds = [9] }, CancellationToken.None);

        Assert.Equal(5, id);
        Received.InOrder(() =>
        {
            _repository.UpdatePurchaseAsync(Arg.Is<InventoryPurchase>(p => p.Receipts.Count == 0 && p.Lines.Count == 2), Arg.Any<CancellationToken>());
            _storage.DeleteAsync("old.jpg", Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task APurchaseCannotHaveMoreThanTenReceipts()
    {
        var uploads = Enumerable.Range(0, InventoryPurchase.MaxReceipts + 1).Select(_ => Upload(Png)).ToArray();

        await Assert.ThrowsAsync<ValidationException>(() => CreateHandler().Handle(Command(uploads), CancellationToken.None));
        await _storage.DidNotReceiveWithAnyArgs().SaveAsync(default!, default!, default);
    }

    [Fact]
    public void Validator_NamesTheItemThatNeedsFixing()
    {
        var result = new SaveInventoryPurchaseCommandValidator().Validate(new SaveInventoryPurchaseCommand
        {
            PurchasedOn = Today,
            Lines = [new("Mustard oil", null, "bottle", 27.96m), new(null, 1m, "lb", 2m), new("Garlic", 1m, "", null)],
        });

        var messages = result.Errors.Select(e => e.ErrorMessage).ToList();
        Assert.Contains("Mustard oil: please enter the quantity bought.", messages);
        Assert.Contains("One of the rows has a quantity or price but no item name.", messages);
        Assert.Contains(messages, m => m.StartsWith("Garlic: please enter a unit", StringComparison.Ordinal));
        Assert.Contains("Garlic: please enter the price paid (0 if it was free).", messages);
    }

    [Fact]
    public void Validator_RequiresAtLeastOneItem() =>
        Assert.Contains(
            new SaveInventoryPurchaseCommandValidator().Validate(new SaveInventoryPurchaseCommand { PurchasedOn = Today }).Errors,
            e => e.ErrorMessage == "Add at least one item with its quantity and price.");

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46, 0, 1 }, ".jpg")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D }, ".png")]
    [InlineData(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P' }, ".webp")]
    [InlineData(new byte[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a', 1, 0, 1, 0, 0, 0 }, null)]
    [InlineData(new byte[] { 0xFF, 0xD8 }, null)]
    public async Task ImageFormat_IsRecognisedFromTheFileContent_AndTheStreamIsRewound(byte[] content, string? extension)
    {
        using var stream = new MemoryStream(content);

        var format = await ImageFormat.DetectAsync(stream);

        Assert.Equal(extension, format?.Extension);
        Assert.Equal(0, stream.Position);
    }
}
