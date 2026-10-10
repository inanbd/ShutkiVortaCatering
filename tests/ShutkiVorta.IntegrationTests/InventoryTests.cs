using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Features.Inventory;
using ShutkiVorta.IntegrationTests.Infrastructure;

namespace ShutkiVorta.IntegrationTests;

/// <summary>Admin → Inventory: purchases with receipt photos, saved item names suggested next time, and the starter data.</summary>
[Collection("app")]
public sealed partial class InventoryTests(TestServers servers)
{
    // A real 1×1 PNG.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    [Theory]
    [InlineData("/admin/inventory")]
    [InlineData("/admin/inventory/edit")]
    [InlineData("/admin/inventory/items")]
    [InlineData("/admin/inventory/items/1")]
    [InlineData("/admin/inventory/receipts/1")]
    public async Task InventoryPages_RequireSignIn(string path)
    {
        var client = servers.Get("Sqlite").CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://shutki.test") });
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task StarterInventory_SuggestsCommonItems_AndShowsSamplePurchases()
    {
        var client = await AdminClientAsync(servers.Get("Sqlite"));

        var list = await client.GetStringAsync("/admin/inventory?q=grocery");
        Assert.Contains("Desi grocery", list);
        Assert.Contains("Loitta shutki", list);
        Assert.Contains("Sample entry", list);

        var form = await client.GetStringAsync("/admin/inventory/edit");
        Assert.Contains("<option value=\"Mustard oil\" data-unit=\"bottle\"></option>", form);
        Assert.Contains("<option value=\"Cilantro\" data-unit=\"bunch\"></option>", form);
        Assert.Contains("name=\"Input.Lines[2].ItemName\"", form); // a few empty rows to fill in

        var items = await client.GetStringAsync("/admin/inventory/items");
        Assert.Contains("Deli containers 16 oz", items);
        Assert.Contains("Kalojira (nigella seeds)", items);
    }

    [Fact]
    public async Task SamplePurchases_AreAddedOnce_AndDoNotComeBackAfterAnAdminDeletesThem()
    {
        var directory = AppFactory.NewDirectory();
        try
        {
            await using (var first = new AppFactory("Sqlite", directory: directory))
            {
                using var scope = first.Services.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<IInventoryRepository>();
                var samples = await repository.SearchPurchasesAsync(new InventoryPurchaseSearch { PageSize = 50 });
                Assert.Equal(3, samples.Purchases.TotalCount);
                foreach (var sample in samples.Purchases.Items)
                {
                    await repository.DeletePurchaseAsync(sample.Id);
                }
            }

            await using (var restarted = new AppFactory("Sqlite", directory: directory))
            {
                using var scope = restarted.Services.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<IInventoryRepository>();
                Assert.Equal(0, (await repository.SearchPurchasesAsync(new InventoryPurchaseSearch())).Purchases.TotalCount);
                Assert.True(await repository.CountItemsAsync() > 20); // the saved names stay
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // Best effort clean-up.
            }
        }
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task Admin_AddsAPurchaseWithAReceiptPhoto_AndNewNamesAreSuggestedNextTime(string provider)
    {
        var factory = servers.Get(provider);
        var client = await AdminClientAsync(factory);
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IInventoryRepository>();
        var today = Today(factory);
        var unique = Guid.NewGuid().ToString("N")[..8];
        var store = $"Test bazaar {unique}";
        var newItem = $"Panch phoron {unique}";

        // A saved name typed in another case and spacing, with the unit left empty, plus a name never used before.
        var form = await client.GetStringAsync("/admin/inventory/edit");
        var added = await PostMultipartAsync(client, "/admin/inventory/edit", form, new()
        {
            ["Input.PurchasedOn"] = today,
            ["Input.Store"] = store,
            ["Input.Lines[0].ItemName"] = "  mustard   OIL ",
            ["Input.Lines[0].Quantity"] = "2",
            ["Input.Lines[0].Unit"] = "",
            ["Input.Lines[0].Price"] = "13.98",
            ["Input.Lines[1].ItemName"] = newItem,
            ["Input.Lines[1].Quantity"] = "0.5",
            ["Input.Lines[1].Unit"] = "LB",
            ["Input.Lines[1].Price"] = "3.49",
            ["Input.Lines[2].ItemName"] = "",
            ["Input.Lines[2].Unit"] = "bag",
        }, ("ReceiptFiles", "IMG_0412.png", Png));
        Assert.Contains("Added 2 items bought on", added);
        Assert.Contains("$17.47 in total", added);
        Assert.Contains(store, added);

        var summary = Assert.Single((await repository.SearchPurchasesAsync(new InventoryPurchaseSearch { Search = unique })).Purchases.Items);
        Assert.Equal(17.47m, summary.Total);
        Assert.Equal(1, summary.ReceiptCount);
        var purchase = (await repository.GetPurchaseAsync(summary.Id))!;
        Assert.Equal(AppFactory.AdminEmail, purchase.CreatedBy);
        Assert.Collection(
            purchase.Lines,
            oil =>
            {
                Assert.Equal("Mustard oil", oil.ItemName);
                Assert.Equal(2m, oil.Quantity);
                Assert.Equal("bottle", oil.Unit);
                Assert.Equal(13.98m, oil.Price);
            },
            spice =>
            {
                Assert.Equal(newItem, spice.ItemName);
                Assert.Equal(0.5m, spice.Quantity);
                Assert.Equal("lb", spice.Unit);
            });

        // "mustard oil" matched the saved item instead of being saved twice; the new name is suggested from now on.
        var items = await repository.GetItemsAsync();
        Assert.Single(items, i => i.NormalizedName == "MUSTARD OIL");
        var newItemId = Assert.Single(items, i => i.Name == newItem).Id;
        Assert.Contains($"<option value=\"{newItem}\" data-unit=\"lb\"></option>", await client.GetStringAsync("/admin/inventory/edit"));

        // The receipt is stored privately and shown to admins only.
        var receipt = Assert.Single(purchase.Receipts);
        Assert.Equal("IMG_0412.png", receipt.OriginalFileName);
        var receiptPath = Path.Combine(factory.ReceiptsDirectory, receipt.FileName);
        Assert.True(File.Exists(receiptPath));
        var photo = await client.GetAsync($"/admin/inventory/receipts/{receipt.Id}");
        Assert.Equal(HttpStatusCode.OK, photo.StatusCode);
        Assert.Equal("image/png", photo.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Png, await photo.Content.ReadAsByteArrayAsync());
        var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://shutki.test") });
        Assert.Equal(HttpStatusCode.Redirect, (await anonymous.GetAsync($"/admin/inventory/receipts/{receipt.Id}")).StatusCode);

        // The item's page shows its price history; it cannot be deleted while a purchase uses it.
        var itemPage = await client.GetStringAsync($"/admin/inventory/items/{newItemId}");
        Assert.Contains(store, itemPage);
        Assert.Contains("$6.98", itemPage); // $3.49 for ½ lb
        Assert.DoesNotContain("Delete this item", itemPage);
        var blocked = await PostFormAsync(client, $"/admin/inventory/items/{newItemId}?handler=Delete", itemPage, []);
        Assert.Contains("is on 1 purchase, so it cannot be deleted", blocked);

        // Correct the purchase and remove the photo: its file is deleted too.
        var edit = await client.GetStringAsync($"/admin/inventory/edit/{summary.Id}");
        Assert.Contains($"src=\"/admin/inventory/receipts/{receipt.Id}\"", edit);
        Assert.Contains("value=\"Mustard oil\"", edit);
        var saved = await PostMultipartAsync(client, $"/admin/inventory/edit/{summary.Id}", edit, new()
        {
            ["Input.PurchasedOn"] = today,
            ["Input.Store"] = store,
            ["Input.Lines[0].ItemName"] = "Mustard oil",
            ["Input.Lines[0].Quantity"] = "3",
            ["Input.Lines[0].Unit"] = "bottle",
            ["Input.Lines[0].Price"] = "20.97",
            ["Input.Lines[1].ItemName"] = newItem,
            ["Input.Lines[1].Quantity"] = "0.5",
            ["Input.Lines[1].Unit"] = "lb",
            ["Input.Lines[1].Price"] = "3.49",
            ["Input.RemoveReceiptIds"] = receipt.Id.ToString(CultureInfo.InvariantCulture),
        });
        Assert.Contains("Saved 2 items bought on", saved);
        purchase = (await repository.GetPurchaseAsync(summary.Id))!;
        Assert.Equal(24.46m, purchase.Total);
        Assert.Empty(purchase.Receipts);
        Assert.False(File.Exists(receiptPath));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/admin/inventory/receipts/{receipt.Id}")).StatusCode);

        // Delete the purchase; the now unused name can then be deleted as well.
        var deleted = await PostFormAsync(client, $"/admin/inventory/edit/{summary.Id}?handler=Delete", edit, []);
        Assert.Contains("Purchase and its receipt photos deleted.", deleted);
        Assert.Null(await repository.GetPurchaseAsync(summary.Id));

        itemPage = await client.GetStringAsync($"/admin/inventory/items/{newItemId}");
        Assert.Contains("Delete this item", itemPage);
        Assert.Contains("Item deleted.", await PostFormAsync(client, $"/admin/inventory/items/{newItemId}?handler=Delete", itemPage, []));
        Assert.Null(await repository.GetItemAsync(newItemId));
    }

    [Fact]
    public async Task AFileThatIsNotAPhoto_IsRefused_AndTheFormKeepsWhatWasTyped()
    {
        var factory = servers.Get("Sqlite");
        var client = await AdminClientAsync(factory);
        var store = $"Refused {Guid.NewGuid():N}"[..20];
        var filesBefore = Directory.Exists(factory.ReceiptsDirectory) ? Directory.GetFiles(factory.ReceiptsDirectory).Length : 0;

        var form = await client.GetStringAsync("/admin/inventory/edit");
        var page = await PostMultipartAsync(client, "/admin/inventory/edit", form, new()
        {
            ["Input.PurchasedOn"] = Today(factory),
            ["Input.Store"] = store,
            ["Input.Lines[0].ItemName"] = "Garlic",
            ["Input.Lines[0].Quantity"] = "5",
            ["Input.Lines[0].Unit"] = "lb",
            ["Input.Lines[0].Price"] = "12.45",
        }, ("ReceiptFiles", "receipt.pdf", "%PDF-1.7 not a photo at all"u8.ToArray()));

        Assert.Contains("\"receipt.pdf\" is not a JPG, PNG or WebP photo.", page);
        Assert.Contains("Please choose them again", page);
        Assert.Contains($"value=\"{store}\"", page);
        Assert.Contains("value=\"Garlic\"", page);

        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IInventoryRepository>();
        Assert.Equal(0, (await repository.SearchPurchasesAsync(new InventoryPurchaseSearch { Search = store })).Purchases.TotalCount);
        Assert.Equal(filesBefore, Directory.Exists(factory.ReceiptsDirectory) ? Directory.GetFiles(factory.ReceiptsDirectory).Length : 0);
    }

    [Fact]
    public async Task IncompleteRows_AreReportedByItemName()
    {
        var factory = servers.Get("Sqlite");
        var client = await AdminClientAsync(factory);

        var form = await client.GetStringAsync("/admin/inventory/edit");
        var page = await PostMultipartAsync(client, "/admin/inventory/edit", form, new()
        {
            ["Input.PurchasedOn"] = Today(factory),
            ["Input.Lines[0].ItemName"] = "Green chili",
            ["Input.Lines[0].Unit"] = "lb",
            ["Input.Lines[0].Price"] = "8.97",
            ["Input.Lines[1].Quantity"] = "2",
        });

        Assert.Contains("Green chili: please enter the quantity bought.", page);
        Assert.Contains("One of the rows has a quantity or price but no item name.", page);
    }

    [Fact]
    public async Task SavedItems_CanBeAdded_AndRenamed_AndNamesAreUniqueWhateverTheCase()
    {
        var factory = servers.Get("Sqlite");
        var client = await AdminClientAsync(factory);
        var name = $"Turmeric {Guid.NewGuid().ToString("N")[..6]}";

        var items = await client.GetStringAsync("/admin/inventory/items");
        var added = await PostFormAsync(client, "/admin/inventory/items?handler=Add", items, new() { ["NewItem.Name"] = name, ["NewItem.Unit"] = "Bag" });
        Assert.Contains($"\"{name}\" saved.", added);
        Assert.Contains($"<option value=\"{name}\" data-unit=\"bag\"></option>", await client.GetStringAsync("/admin/inventory/edit"));

        var duplicate = await PostFormAsync(client, "/admin/inventory/items?handler=Add", added, new() { ["NewItem.Name"] = $" {name.ToUpperInvariant()} ", ["NewItem.Unit"] = "lb" });
        Assert.Contains($"\"{name}\" is already saved.", duplicate);

        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IInventoryRepository>();
        var item = (await repository.GetItemByNormalizedNameAsync(name.ToUpperInvariant()))!;
        var itemPage = await client.GetStringAsync($"/admin/inventory/items/{item.Id}");
        var renamed = await PostFormAsync(client, $"/admin/inventory/items/{item.Id}", itemPage, new() { ["Input.Name"] = $"{name} powder", ["Input.Unit"] = "jar" });
        Assert.Contains("Item saved.", renamed);

        item = (await repository.GetItemAsync(item.Id))!;
        Assert.Equal($"{name} powder", item.Name);
        Assert.Equal("jar", item.Unit);
    }

    private static string Today(AppFactory factory) =>
        factory.Services.GetRequiredService<IDateTimeProvider>().BusinessNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static async Task<HttpClient> AdminClientAsync(AppFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://shutki.test") });
        var login = await client.GetStringAsync("/account/login");
        var response = await client.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = AppFactory.AdminEmail,
            ["Input.Password"] = AppFactory.AdminPassword,
            ["__RequestVerificationToken"] = Token(login),
        }));
        response.EnsureSuccessStatusCode();
        return client;
    }

    /// <summary>Posts a form using the antiforgery token from <paramref name="page"/> and returns the page shown afterwards.</summary>
    private static async Task<string> PostFormAsync(HttpClient client, string url, string page, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = Token(page);
        var response = await client.PostAsync(url, new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    /// <summary>Posts a form with file uploads, as the browser does for the inventory form.</summary>
    private static async Task<string> PostMultipartAsync(
        HttpClient client, string url, string page, Dictionary<string, string> fields, params (string Field, string FileName, byte[] Content)[] files)
    {
        using var content = new MultipartFormDataContent();
        fields["__RequestVerificationToken"] = Token(page);
        foreach (var (name, value) in fields)
        {
            content.Add(new StringContent(value), name);
        }

        foreach (var (field, fileName, bytes) in files)
        {
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            content.Add(file, field, fileName);
        }

        var response = await client.PostAsync(url, content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private static string Token(string html) => AntiforgeryToken().Match(html).Groups[1].Value;

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryToken();
}
