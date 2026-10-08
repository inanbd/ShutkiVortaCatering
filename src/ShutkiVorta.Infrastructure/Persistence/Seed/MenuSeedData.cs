using ShutkiVorta.Domain.Menu;

namespace ShutkiVorta.Infrastructure.Persistence.Seed;

/// <summary>Starter menu: three signature shutki vortas and six everyday vortas, sold by the pound.</summary>
internal static class MenuSeedData
{
    public static IReadOnlyList<MenuItemDetails> Items =>
    [
        new()
        {
            Name = "Loitta Shutki Vorta",
            BengaliName = "লইট্টা শুঁটকি ভর্তা",
            Category = MenuCategory.ShutkiVorta,
            ShortDescription = "Our signature. Dried Bombay duck, flame-roasted and hand-pounded with red onion, garlic, green chilies and raw mustard oil.",
            Description = """
                Loitta shutki — sun-dried Bombay duck from the Bay of Bengal coast — is the heart of Chattogram home cooking, and the dish our kitchen is known for. We soak and clean every piece, dry-roast it over an open flame until it turns golden and smoky, then pound it on a traditional shil-pata with sliced red onion, garlic, roasted dried red chilies and fresh green chilies.

                A generous drizzle of raw mustard oil and a handful of cilantro finish the vorta: deeply savory, gently funky and properly hot. Enjoy it the classic way, mixed into steaming white rice with a squeeze of lime, or alongside dal and khichuri.

                Made fresh to order in Dallas and sold by the pound. A half pound feeds two to three people as a side.
                """,
            Ingredients = "Dried loitta (Bombay duck), red onion, garlic, dried red chili, green chili, cilantro, mustard oil, salt",
            PricePerUnit = 24.99m,
            MinimumQuantity = 0.5m,
            QuantityStep = 0.5m,
            SpiceLevel = 4,
            ImageUrl = "/images/menu/loitta-shutki-vorta.webp",
            ImageAlt = "Loitta shutki vorta — pounded dried Bombay duck with onion and chili on a white plate",
            ImageCredit = "Photo: Ferdous / Wikimedia Commons, CC BY-SA 3.0",
            IsFeatured = true,
            SortOrder = 1,
            MetaTitle = "Loitta Shutki Vorta by the Pound in Dallas, TX",
            MetaDescription = "Authentic Bangladeshi loitta shutki vorta (dried Bombay duck), flame-roasted and hand-pounded. Order by the lb for pickup or delivery in Dallas.",
        },
        new()
        {
            Name = "Chingri Shutki Vorta",
            BengaliName = "চিংড়ি শুঁটকি ভর্তা",
            Category = MenuCategory.ShutkiVorta,
            ShortDescription = "Tiny sun-dried shrimp toasted until crisp, then pounded with garlic, shallots and green chili. Sweet, smoky and addictive.",
            Description = """
                Chingri shutki is the gateway to the world of dried fish: naturally sweet, intensely aromatic and loved even by first-timers. We toast the small dried shrimp in a dry pan until crisp and fragrant, then pound them coarsely with shallots, plenty of garlic, roasted dried chilies and fresh green chilies.

                Mustard oil brings everything together with that unmistakable Bengali pungency. Spread it on hot rice, roll it into a roti, or serve it as part of a vorta platter for guests.

                Made fresh to order in Dallas and sold by the pound.
                """,
            Ingredients = "Dried shrimp, shallot, garlic, dried red chili, green chili, cilantro, mustard oil, salt",
            PricePerUnit = 26.99m,
            MinimumQuantity = 0.5m,
            QuantityStep = 0.5m,
            SpiceLevel = 3,
            ImageUrl = "/images/menu/chingri-shutki-vorta.webp",
            ImageAlt = "Chingri shutki vorta — pounded dried shrimp with onion and green chili",
            ImageCredit = "Photo: Ferdous / Wikimedia Commons, CC BY-SA 3.0",
            IsFeatured = true,
            SortOrder = 2,
            MetaTitle = "Chingri Shutki Vorta (Dried Shrimp) | Order in Dallas",
            MetaDescription = "Bangladeshi chingri shutki vorta made with toasted dried shrimp, garlic, chili and mustard oil. Order by the pound for pickup or delivery in Dallas, TX.",
        },
        new()
        {
            Name = "Chepa Shutki Vorta",
            BengaliName = "চ্যাপা শুঁটকি ভর্তা",
            Category = MenuCategory.ShutkiVorta,
            ShortDescription = "The bold one. Fermented puti fish from Mymensingh and Sylhet, cooked down with heaps of garlic and fiery chilies.",
            Description = """
                Chepa shutki is fermented puti (pool barb) fish, a treasured delicacy of greater Mymensingh and Sylhet. Its aroma is powerful and its flavor is unforgettable — for those who grew up with it, nothing tastes more like home.

                We roast the chepa gently, then cook and mash it with a mountain of garlic, red onion and both dried and fresh chilies until it becomes a rich, deeply savory vorta. It is hot, pungent and absolutely traditional.

                Recommended for experienced shutki lovers. Made fresh to order in Dallas and sold by the pound.
                """,
            Ingredients = "Fermented puti fish (chepa shutki), garlic, red onion, dried red chili, green chili, mustard oil, salt",
            PricePerUnit = 27.99m,
            MinimumQuantity = 0.5m,
            QuantityStep = 0.5m,
            SpiceLevel = 5,
            ImageUrl = "/images/menu/chepa-shutki-vorta.webp",
            ImageAlt = "Chepa shutki vorta — dark, spicy fermented fish vorta served on an orange plate",
            ImageCredit = "Photo: Deeptoe26 / Wikimedia Commons, CC BY-SA 4.0",
            IsFeatured = true,
            SortOrder = 3,
            MetaTitle = "Chepa Shutki Vorta (Fermented Fish) in Dallas, TX",
            MetaDescription = "Traditional Mymensingh-style chepa shutki vorta made with fermented puti fish, garlic and chili. Order by the pound in Dallas for pickup or delivery.",
        },
        new()
        {
            Name = "Aloo Vorta",
            BengaliName = "আলু ভর্তা",
            Category = MenuCategory.ClassicVorta,
            ShortDescription = "The comfort classic: buttery mashed potatoes with mustard oil, fried onion, green chili and fresh cilantro.",
            Description = """
                No Bengali meal feels complete without aloo vorta. We boil starchy potatoes until fluffy, mash them by hand and fold in raw mustard oil, crispy fried onion, chopped green chilies, fried dried red chili and a little cilantro.

                Simple, soulful and endlessly comforting — perfect with dal and rice, khichuri on a rainy day, or as a crowd-pleasing side for parties of every size.

                Made fresh to order in Dallas and sold by the pound.
                """,
            Ingredients = "Potato, mustard oil, onion, green chili, dried red chili, cilantro, salt",
            PricePerUnit = 11.99m,
            MinimumQuantity = 0.5m,
            QuantityStep = 0.5m,
            SpiceLevel = 2,
            ImageUrl = "/images/menu/aloo-vorta.webp",
            ImageAlt = "Aloo vorta — Bengali mashed potato with green chili in a glass bowl",
            ImageCredit = "Photo: Sm faysal / Wikimedia Commons, CC BY-SA 4.0",
            IsFeatured = true,
            SortOrder = 10,
            MetaTitle = "Aloo Vorta (Bengali Mashed Potato) | Dallas Catering",
            MetaDescription = "Homestyle Bangladeshi aloo vorta with mustard oil, fried onion and green chili. Order by the pound for pickup or delivery in Dallas, TX.",
        },
        new()
        {
            Name = "Begun Vorta",
            BengaliName = "বেগুন ভর্তা",
            Category = MenuCategory.ClassicVorta,
            ShortDescription = "Whole eggplant charred over an open flame, then mashed with onion, tomato, chili and mustard oil. Smoky and silky.",
            Description = """
                Our begun vorta starts the old-fashioned way: whole eggplants are charred directly over a flame until the skin blisters and the flesh turns soft and smoky. We peel them by hand and mash the flesh with finely chopped red onion, tomato, green chilies, cilantro and a generous splash of mustard oil.

                The result is silky, smoky and fresh — a beloved vorta that pairs beautifully with rice, paratha or luchi.

                Made fresh to order in Dallas and sold by the pound.
                """,
            Ingredients = "Eggplant, red onion, tomato, green chili, cilantro, garlic, mustard oil, salt",
            PricePerUnit = 13.99m,
            MinimumQuantity = 0.5m,
            QuantityStep = 0.5m,
            SpiceLevel = 2,
            ImageUrl = "/images/menu/begun-vorta.webp",
            ImageAlt = "Begun vorta — smoky mashed eggplant with onion and cilantro on a floral plate",
            ImageCredit = "Photo: Atudu / Wikimedia Commons, CC BY-SA 4.0",
            IsFeatured = true,
            SortOrder = 11,
            MetaTitle = "Begun Vorta (Smoky Eggplant) | Order in Dallas, TX",
            MetaDescription = "Flame-charred Bangladeshi begun vorta with onion, tomato, chili and mustard oil. Order by the pound for pickup or delivery in Dallas.",
        },
        new()
        {
            Name = "Dal Vorta",
            BengaliName = "ডাল ভর্তা",
            Category = MenuCategory.ClassicVorta,
            ShortDescription = "Red lentils cooked thick, dry-roasted and mashed with fried garlic, onion, dried chili and mustard oil.",
            Description = """
                A humble lentil becomes something special in dal vorta. We cook masoor dal until just tender, dry it out in the pan so every grain toasts lightly, then mash it with golden fried garlic, onion, roasted dried red chilies and mustard oil.

                Nutty, earthy and full of flavor — a protein-rich vorta that is naturally vegan and gluten-free.

                Made fresh to order in Dallas and sold by the pound.
                """,
            Ingredients = "Red lentils (masoor dal), garlic, onion, dried red chili, green chili, cilantro, mustard oil, salt",
            PricePerUnit = 12.99m,
            MinimumQuantity = 0.5m,
            QuantityStep = 0.5m,
            SpiceLevel = 2,
            ImageUrl = "/images/menu/dal-vorta.webp",
            ImageAlt = "Dal vorta — mashed red lentils with green chili in a glass bowl",
            ImageCredit = "Photo: Sm faysal / Wikimedia Commons, CC BY-SA 4.0",
            IsFeatured = false,
            SortOrder = 12,
            MetaTitle = "Dal Vorta (Mashed Red Lentils) | Dallas, TX",
            MetaDescription = "Bangladeshi dal vorta made with roasted red lentils, fried garlic and mustard oil. Vegan and gluten-free. Order by the pound in Dallas.",
        },
        new()
        {
            Name = "Tomato Vorta",
            BengaliName = "টমেটো ভর্তা",
            Category = MenuCategory.ClassicVorta,
            ShortDescription = "Fire-roasted tomatoes mashed with garlic, green chili, cilantro and mustard oil. Tangy, bright and a little smoky.",
            Description = """
                Ripe tomatoes are roasted until their skins blister and their juices turn sweet and smoky. We peel and mash them with roasted garlic, green chilies, red onion, cilantro and a touch of mustard oil.

                Bright, tangy and refreshing, tomato vorta balances the richer shutki vortas on any table and is a favorite with kids and grown-ups alike.

                Made fresh to order in Dallas and sold by the pound.
                """,
            Ingredients = "Tomato, garlic, green chili, red onion, cilantro, mustard oil, salt",
            PricePerUnit = 12.99m,
            MinimumQuantity = 0.5m,
            QuantityStep = 0.5m,
            SpiceLevel = 2,
            ImageUrl = "/images/menu/tomato-vorta.webp",
            ImageAlt = "Tomato vorta — roasted tomato mash with green chili and cilantro in a white leaf-shaped dish",
            ImageCredit = "Photo: Sm faysal / Wikimedia Commons, CC BY-SA 4.0",
            IsFeatured = false,
            SortOrder = 13,
            MetaTitle = "Tomato Vorta (Roasted Tomato Mash) | Dallas, TX",
            MetaDescription = "Fire-roasted Bangladeshi tomato vorta with garlic, chili and mustard oil. Order by the pound for pickup or delivery in Dallas, Texas.",
        },
        new()
        {
            Name = "Kalojira Vorta",
            BengaliName = "কালোজিরা ভর্তা",
            Category = MenuCategory.ClassicVorta,
            ShortDescription = "Toasted black seed (nigella) ground with garlic, green chili and mustard oil. Aromatic, nutty and wonderfully unique.",
            Description = """
                Kalojira — black seed or nigella — is cherished across Bangladesh for its aroma and its place in traditional wellness. We gently toast the seeds, then grind them on the shil-pata with garlic, green chilies, a little onion and mustard oil into a dark, fragrant paste.

                Just a spoonful with rice is enough to wake up the whole plate. A distinctive vorta for the adventurous palate.

                Made fresh to order in Dallas and sold by the pound.
                """,
            Ingredients = "Black seed (kalojira / nigella), garlic, green chili, onion, mustard oil, salt",
            PricePerUnit = 16.99m,
            MinimumQuantity = 0.5m,
            QuantityStep = 0.5m,
            SpiceLevel = 3,
            ImageUrl = "/images/menu/kalojira-vorta.webp",
            ImageAlt = "Kalojira vorta — dark ground black seed paste with garlic and chili",
            ImageCredit = "Photo: Ferdous / Wikimedia Commons, CC BY-SA 3.0",
            IsFeatured = false,
            SortOrder = 14,
            MetaTitle = "Kalojira Vorta (Black Seed Vorta) | Dallas, TX",
            MetaDescription = "Aromatic Bangladeshi kalojira (black seed) vorta ground with garlic, green chili and mustard oil. Order by the pound in Dallas.",
        },
        new()
        {
            Name = "Shim Vorta",
            BengaliName = "শিম ভর্তা",
            Category = MenuCategory.ClassicVorta,
            ShortDescription = "Tender flat beans boiled and pounded with garlic, green chili, onion and mustard oil. A winter-market favorite.",
            Description = """
                Shim — the flat green bean that fills Bangladeshi markets every winter — makes one of the freshest vortas of all. We boil the beans until tender, then pound them coarsely with garlic, green chilies, onion, cilantro and mustard oil.

                Light, green and gently spicy, shim vorta brings a garden-fresh note to any vorta spread.

                Made fresh to order in Dallas and sold by the pound.
                """,
            Ingredients = "Flat beans (shim), garlic, green chili, onion, cilantro, mustard oil, salt",
            PricePerUnit = 13.99m,
            MinimumQuantity = 0.5m,
            QuantityStep = 0.5m,
            SpiceLevel = 2,
            ImageUrl = "/images/menu/shim-vorta.webp",
            ImageAlt = "Shim vorta — pounded green flat beans with garlic and chili on a plate",
            ImageCredit = "Photo: Sm faysal / Wikimedia Commons, CC BY-SA 4.0",
            IsFeatured = false,
            SortOrder = 15,
            MetaTitle = "Shim Vorta (Flat Bean Vorta) | Dallas, TX",
            MetaDescription = "Fresh Bangladeshi shim (flat bean) vorta with garlic, green chili and mustard oil. Order by the pound for pickup or delivery in Dallas.",
        },
    ];
}
