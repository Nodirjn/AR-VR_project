using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

// The Car Explorer's part registry and structure tree: vehicle -> systems -> assemblies -> sub-assemblies -> parts.
// Nodes carrying CarPartInfo define the tree under them; CarConcept's own glTF nodes and the untagged V6 are grouped
// by the name rules below, so the explorer works with and without the generated internals.
public sealed partial class CarExplorerController
{
    // How a node moves away from its siblings when its parent comes apart, in the car's frame: x outward (to the
    // assembly's side, or the car's right on the centre line), y up, z forward. Axle: along the wheel's own axle.
    private enum SpreadMode { Auto, Anchor, Direction, AxleOut, AxleIn }

    private struct SpreadHint
    {
        public SpreadMode Mode;
        public Vector3 Car;

        public static readonly SpreadHint Auto = new SpreadHint { Mode = SpreadMode.Auto };
        public static readonly SpreadHint Stay = new SpreadHint { Mode = SpreadMode.Anchor };
        public static readonly SpreadHint AxleOut = new SpreadHint { Mode = SpreadMode.AxleOut };
        public static readonly SpreadHint AxleIn = new SpreadHint { Mode = SpreadMode.AxleIn };

        public static SpreadHint Dir(float outward, float up, float forward)
        {
            return new SpreadHint { Mode = SpreadMode.Direction, Car = new Vector3(outward, up, forward) };
        }
    }

    // A group inside a rule-based assembly (CarConcept's own nodes and the untagged V6): matched by name terms,
    // nested by its parent's key.
    private sealed class GroupRule
    {
        public string Key;
        public string Parent;
        public string English;
        public string Uzbek;
        public string DescriptionEnglish;
        public string DescriptionUzbek;
        public string[] Terms;
        public SpreadHint Hint;
    }

    // Name, description and spread of a single part, matched by name terms (lower case, first match wins).
    private sealed class PartRule
    {
        public string[] Terms;
        public string English;
        public string Uzbek;
        public string DescriptionEnglish;
        public string DescriptionUzbek;
        public SpreadHint Hint;
    }

    private static GroupRule G(string key, string parent, string english, string uzbek, string descriptionEnglish,
        string descriptionUzbek, SpreadHint hint, params string[] terms)
    {
        return new GroupRule
        {
            Key = key, Parent = parent, English = english, Uzbek = uzbek, DescriptionEnglish = descriptionEnglish,
            DescriptionUzbek = descriptionUzbek, Hint = hint, Terms = terms
        };
    }

    private static PartRule P(string english, string uzbek, string descriptionEnglish, string descriptionUzbek,
        SpreadHint hint, params string[] terms)
    {
        return new PartRule
        {
            English = english, Uzbek = uzbek, DescriptionEnglish = descriptionEnglish,
            DescriptionUzbek = descriptionUzbek, Hint = hint, Terms = terms
        };
    }

    private static readonly string[] SystemDescriptionsEnglish =
    {
        "Outer skin and glazing: panels, glass and lights.",
        "Load-carrying frame: pillars, cage, underbody and axles.",
        "Doors and the other opening panels.",
        "Cabin: seats, cockpit and floor.",
        "Road wheels with their brakes.",
        "Discs, calipers and lines that stop the car.",
        "Springs, dampers and arms that hold the wheels to the body.",
        "Rack, column and linkage that turn the front wheels.",
        "The engine and the systems that make its power.",
        "Gearbox, shafts and differentials that carry the power to the wheels.",
        "Manifolds, catalysts and silencers that carry the exhaust gas away.",
        "Tank, pump and lines that feed the engine.",
        "Radiator, fans and hoses that keep the engine at temperature.",
        "Battery, wiring and control units."
    };

    private static readonly string[] SystemDescriptionsUzbek =
    {
        "Tashqi qoplama va oynalar: panellar, shisha va chiroqlar.",
        "Yuk ko‘taruvchi karkas: ustunlar, karkas, kuzov tagi va o‘qlar.",
        "Eshiklar va boshqa ochiladigan panellar.",
        "Salon: o‘rindiqlar, boshqaruv joyi va pol.",
        "Tormozlari bilan g‘ildiraklar.",
        "Avtomobilni to‘xtatuvchi disklar, supportlar va quvurlar.",
        "G‘ildiraklarni kuzovga bog‘lovchi prujinalar, amortizatorlar va richaglar.",
        "Old g‘ildiraklarni buruvchi reyka, ustun va tortqilar.",
        "Dvigatel va uning quvvat hosil qiluvchi tizimlari.",
        "Quvvatni g‘ildiraklarga uzatuvchi uzatmalar qutisi, vallar va differensiallar.",
        "Ishlatilgan gazni chiqaruvchi kollektorlar, katalizatorlar va glushitellar.",
        "Dvigatelni yoqilg‘i bilan ta’minlovchi bak, nasos va quvurlar.",
        "Dvigatel haroratini ushlab turuvchi radiator, ventilyatorlar va shlanglar.",
        "Akkumulyator, simlar va boshqaruv bloklari."
    };

    // Groups of CarConcept's rule-based assemblies, by assembly kind. Parts that match no group sit directly under
    // the assembly.
    private static readonly Dictionary<AssemblyKind, GroupRule[]> AssemblyGroups =
        new Dictionary<AssemblyKind, GroupRule[]>
    {
        {
            AssemblyKind.Wheel, new[]
            {
                G("road", null, "Road wheel", "G‘ildirak (shina va disk)",
                    "Tyre mounted on its rim; bolts to the hub as one unit.",
                    "Diskka o‘rnatilgan shina; gupchakka bitta yig‘ma bo‘lib mahkamlanadi.",
                    SpreadHint.AxleOut, "rim", "node-", "tyre", "tire"),
                G("brake", null, "Disc brake", "Diskli tormoz",
                    "Brake disc and caliper that slow this wheel.",
                    "Shu g‘ildirakni sekinlatuvchi tormoz diski va supporti.",
                    SpreadHint.AxleIn, "disc", "pad", "caliper")
            }
        },
        {
            AssemblyKind.Door, new[]
            {
                G("shell", null, "Door shell", "Eshik korpusi",
                    "Painted outer skin of the door with its lower trim.",
                    "Eshikning bo‘yalgan tashqi qoplamasi va pastki bezagi.",
                    SpreadHint.Stay, "color1", "color2"),
                G("window", null, "Window and seal", "Oyna va zichlagich",
                    "Side window glass and the rubber seal around it.",
                    "Yon oyna va uning atrofidagi rezina zichlagich.",
                    SpreadHint.Dir(0.15f, 1f, 0f), "window"),
                G("mirror", null, "Door mirror", "Yon ko‘zgu",
                    "Exterior mirror: housing, cap and glass.",
                    "Tashqi ko‘zgu: korpus, qopqoq va oyna.",
                    SpreadHint.Dir(1f, 0.35f, 0.5f), "mirror"),
                G("handles", null, "Door handles", "Eshik dastalari",
                    "Handles that open the door.",
                    "Eshikni ochuvchi dastalar.",
                    SpreadHint.Dir(1f, -0.2f, -0.3f), "handle"),
                G("trim", null, "Inner trim", "Ichki qoplama",
                    "Inner door card, armrest and trim panels.",
                    "Eshikning ichki paneli, tirsaklik va bezak panellari.",
                    SpreadHint.Dir(-1f, 0f, 0f), "interiordoor")
            }
        },
        {
            AssemblyKind.Front, new[]
            {
                G("hood", null, "Hood", "Kapot",
                    "Hood skin with its inner reinforcement panels.",
                    "Kapot qoplamasi va ichki kuchaytirish panellari.",
                    SpreadHint.Dir(0f, 1f, 0f), "=bodyhood", "hoodinterior"),
                G("lights", null, "Headlights", "Old faralar",
                    "LED headlamp units.", "LED old fara bloklari.",
                    SpreadHint.Dir(0f, 0.2f, 1f), "headlight")
            }
        },
        {
            AssemblyKind.Rear, new[]
            {
                G("panels", null, "Rear body panels", "Orqa kuzov panellari",
                    "Painted rear panels with their inner panels.",
                    "Bo‘yalgan orqa panellar va ichki panellar.",
                    SpreadHint.Stay, "rearpanels"),
                G("tailgate", null, "Tailgate", "Orqa lyuk",
                    "Rear hatch with its window.", "Orqa oynasi bilan orqa lyuk.",
                    SpreadHint.Dir(0f, 1f, -0.2f), "rearhatch", "rearwindow"),
                G("lights", null, "Rear lights", "Orqa chiroqlar",
                    "Tail lights, their panels and the rear turn signals.",
                    "Orqa chiroqlar, ularning panellari va burilish chiroqlari.",
                    SpreadHint.Dir(0f, 0.1f, -1f), "taillight", "turnsignal")
            }
        },
        {
            AssemblyKind.Windscreen, new[]
            {
                G("wipers", null, "Wipers", "Oyna tozalagichlar",
                    "Wiper arms and their base.", "Tozalagich richaglari va ularning asosi.",
                    SpreadHint.Dir(0f, 0.4f, 1f), "wiper")
            }
        },
        {
            AssemblyKind.Seats, new[]
            {
                G("frames", null, "Seat frames", "O‘rindiq ramalari",
                    "Steel frames that carry the cushions and fix the seats to the floor.",
                    "Yostiqlarni tutib, o‘rindiqlarni polga mahkamlovchi po‘lat ramalar.",
                    SpreadHint.Dir(0f, -1f, 0f), "frame")
            }
        },
        {
            AssemblyKind.Cockpit, new[]
            {
                G("dash", null, "Dashboard", "Asboblar paneli",
                    "Dashboard panels and the instrument binnacle.",
                    "Asboblar paneli qismlari va asboblar qutisi.",
                    SpreadHint.Stay, "dashmid", "dashsides", "steeringdash"),
                G("steering", null, "Steering", "Rul boshqaruvi",
                    "Steering wheel and column.", "Rul g‘ildiragi va rul ustuni.",
                    SpreadHint.Dir(0f, 0.25f, -1f)),
                G("wheel", "steering", "Steering wheel", "Rul g‘ildiragi",
                    "Hub, rim, grips and emblem of the steering wheel.",
                    "Rul g‘ildiragining gupchagi, halqasi, tutqichlari va emblemasi.",
                    SpreadHint.Dir(0f, 0.2f, -1f), "steeringcylinder", "steeringemblem", "steeringwheel",
                    "steeringhandle"),
                G("rim", "wheel", "Rim and spokes", "Rul halqasi va kegaylari",
                    "Segments of the wheel rim and its spokes.",
                    "Rul halqasi bo‘laklari va kegaylari.",
                    SpreadHint.Dir(0f, 0f, -1f), "steeringwheel"),
                G("grips", "wheel", "Grips", "Tutqichlar",
                    "Padded grips on the rim.", "Halqadagi yumshoq tutqichlar.",
                    SpreadHint.Dir(0f, 0f, -1f), "steeringhandle"),
                G("column", "steering", "Steering column", "Rul ustuni",
                    "Column and its shroud that carry the wheel.",
                    "Rul g‘ildiragini tutuvchi ustun va uning qoplamasi.",
                    SpreadHint.Stay, "steeringbase", "dashcolumn"),
                G("pedals", null, "Pedal box", "Pedallar bloki",
                    "Accelerator and brake pedals.", "Gaz va tormoz pedallari.",
                    SpreadHint.Dir(0f, -0.4f, 1f), "pedal"),
                G("accelerator", "pedals", "Accelerator pedal", "Gaz pedali",
                    "Pedal that sets the engine power.", "Dvigatel quvvatini boshqaruvchi pedal.",
                    SpreadHint.Dir(1f, 0f, 0f), "pedalaccel"),
                G("brakepedal", "pedals", "Brake pedal", "Tormoz pedali",
                    "Pedal that works the brakes.", "Tormozlarni boshqaruvchi pedal.",
                    SpreadHint.Dir(-1f, 0f, 0f), "pedalbrake")
            }
        },
        {
            AssemblyKind.Engine, new[]
            {
                G("short", null, "Short block", "Silindrlar bloki yig‘masi",
                    "Block, crankshaft, pistons and oil pan: the bottom end of the engine.",
                    "Blok, tirsakli val, porshenlar va moy karteri: dvigatelning pastki qismi.",
                    SpreadHint.Stay, "engine block", "side cover", "oil pan", "crankshaft", "piston"),
                G("pistons", "short", "Pistons and connecting rods", "Porshenlar va shatunlar",
                    "Six pistons that turn the combustion pressure into crankshaft rotation.",
                    "Yonish bosimini tirsakli val aylanishiga aylantiruvchi oltita porshen.",
                    SpreadHint.Dir(0f, 1f, 0f), "piston"),
                G("heads", null, "Cylinder heads", "Silindrlar kallaklari",
                    "Two heads that close the cylinders, with their valve covers.",
                    "Silindrlarni yopuvchi ikkita kallak va klapan qopqoqlari.",
                    SpreadHint.Auto, "cylinder head", "valve cover"),
                G("valvetrain", null, "Valve train", "Gaz taqsimlash mexanizmi",
                    "Camshafts, rocker arms, valves and springs that time the gas flow.",
                    "Gaz oqimini boshqaruvchi taqsimlash vallari, koromislolar, klapanlar va prujinalar.",
                    SpreadHint.Dir(0f, 1f, 0f), "cam", "rocker", "engine valve"),
                G("camshafts", "valvetrain", "Camshafts", "Taqsimlash vallari",
                    "Shafts whose cams open the valves.", "Mushtchalari klapanlarni ochuvchi vallar.",
                    SpreadHint.Auto, "cam shaft0", "cam shaft", "cam"),
                G("caps", "valvetrain", "Camshaft caps", "Taqsimlash vali qopqoqlari",
                    "Caps that hold the camshafts in the heads.",
                    "Taqsimlash vallarini kallakda ushlab turuvchi qopqoqlar.",
                    SpreadHint.Auto, "cam shaft retainer"),
                G("rockers", "valvetrain", "Rocker arms", "Koromislolar",
                    "Levers that pass the cam lift to the valves.",
                    "Mushtcha harakatini klapanlarga uzatuvchi richaglar.",
                    SpreadHint.Auto, "rocker arm assembly"),
                G("valves", "valvetrain", "Valves", "Klapanlar",
                    "Intake and exhaust valves that open and close the ports.",
                    "Kanallarni ochib-yopuvchi kiritish va chiqarish klapanlari.",
                    SpreadHint.Auto, "engine valve"),
                G("springs", "valvetrain", "Valve springs", "Klapan prujinalari",
                    "Springs that close the valves again.", "Klapanlarni qayta yopuvchi prujinalar.",
                    SpreadHint.Auto, "backing spring"),
                G("intake", null, "Intake and supercharging", "Kiritish va nagnetatsiya",
                    "Air filters, superchargers, hoses and the intake manifold.",
                    "Havo filtrlari, nagnetatorlar, shlanglar va kiritish kollektori.",
                    SpreadHint.Dir(0f, 1f, 0.2f), "intake", "air filter", "supercharger", "turbo", "horse",
                    "hose"),
                G("exhaust", null, "Exhaust manifolds", "Chiqarish kollektorlari",
                    "Collect the exhaust gas from each bank of cylinders.",
                    "Har bir silindrlar qatoridan ishlatilgan gazni yig‘adi.",
                    SpreadHint.Auto, "exhaust"),
                G("drive", null, "Accessory drive", "Yordamchi agregatlar uzatmasi",
                    "Belt pulleys that drive the engine's accessories.",
                    "Dvigatelning yordamchi agregatlarini aylantiruvchi tasma shkivlari.",
                    SpreadHint.Auto, "belt wheel")
            }
        }
    };

    // CarConcept's own parts (glTF node names, lower case) and the V6's (CAD names).
    private static readonly PartRule[] CarPartRules =
    {
        P("Tyre", "Shina", "Rubber tyre: grips the road and cushions the ride.",
            "Rezina shina: yo‘lni ushlaydi va zarbalarni yumshatadi.", SpreadHint.Stay, "node-", "tyre", "tire"),
        P("Rim", "G‘ildirak diski", "Alloy rim that carries the tyre.", "Shinani tutib turuvchi qotishma disk.",
            SpreadHint.AxleOut, "rim"),
        P("Brake disc", "Tormoz diski", "Disc that turns with the wheel; the pads clamp it to slow the car.",
            "G‘ildirak bilan aylanadigan disk; kolodkalar uni siqib avtomobilni sekinlatadi.", SpreadHint.Stay,
            "brakedisc"),
        P("Brake caliper", "Tormoz supporti", "Caliper with the pads that squeeze the disc.",
            "Diskni siquvchi kolodkali support.", SpreadHint.AxleIn, "brakepad", "caliper"),
        P("Window seal", "Oyna zichlagichi", "Rubber seal that keeps water and wind out.",
            "Suv va shamolni o‘tkazmaydigan rezina zichlagich.", SpreadHint.Stay, "doorlwindowgasket",
            "doorrwindowgasket"),
        P("Side window", "Yon oyna", "Tempered glass side window.", "Toblangan shishadan yon oyna.",
            SpreadHint.Dir(0f, 1f, 0f), "doorlwindow", "doorrwindow"),
        P("Mirror cap", "Ko‘zgu qopqog‘i", "Body-coloured cap of the mirror housing.",
            "Ko‘zgu korpusining kuzov rangidagi qopqog‘i.", SpreadHint.Dir(1f, 0.3f, 0f), "mirrorcolor1"),
        P("Mirror housing", "Ko‘zgu korpusi", "Housing and arm that carry the mirror glass.",
            "Ko‘zgu oynasini tutuvchi korpus va tirgak.", SpreadHint.Stay, "mirrorcolor2"),
        P("Mirror glass", "Ko‘zgu oynasi", "Mirror glass that shows the road behind.",
            "Orqadagi yo‘lni ko‘rsatuvchi ko‘zgu oynasi.", SpreadHint.Dir(-0.3f, 0f, -1f), "mirror"),
        P("Door handle", "Eshik dastasi", "Handle that releases the door latch.",
            "Eshik qulfini ochuvchi dasta.", SpreadHint.Auto, "doorlhandle", "doorrhandle"),
        P("Inner trim panel", "Ichki qoplama paneli", "Trim panel on the inside of the door.",
            "Eshik ichki tomonidagi bezak paneli.", SpreadHint.Auto, "interiordoor"),
        P("Lower trim", "Pastki bezak", "Contrasting trim along the bottom of the door.",
            "Eshik pastidagi rangli bezak.", SpreadHint.Dir(0.3f, -1f, 0f), "doorlcolor2", "doorrcolor2"),
        P("Outer panel", "Tashqi panel", "Pressed outer skin of the door.",
            "Eshikning shtamplangan tashqi qoplamasi.", SpreadHint.Stay, "doorlcolor1", "doorrcolor1"),
        P("Inner hood panel", "Kapotning ichki paneli", "Inner panel that stiffens the hood.",
            "Kapotni qattiqlashtiruvchi ichki panel.", SpreadHint.Dir(0f, -1f, 0f), "hoodinterior01"),
        P("Hood reinforcement", "Kapot kuchaytirgichi", "Inner reinforcement of the hood.",
            "Kapotning ichki kuchaytirgichi.", SpreadHint.Dir(0f, -1f, 0f), "hoodinterior02"),
        P("Upper grille", "Yuqori panjara", "Grille that lets air in to the radiator.",
            "Radiatorga havo o‘tkazuvchi panjara.", SpreadHint.Dir(0f, 0.3f, 1f), "topgrill"),
        P("Front underside", "Old qism osti", "Underside panel of the front section.",
            "Old qismning pastki paneli.", SpreadHint.Dir(0f, -1f, 0.3f), "hoodunder"),
        P("Headlights", "Old faralar", "LED headlamp units.", "LED old fara bloklari.", SpreadHint.Auto,
            "headlight"),
        P("Hood and front panels", "Kapot va old panellar", "Painted front skin: hood, front wings and bumper.",
            "Bo‘yalgan old qoplama: kapot, old qanotlar va bamper.", SpreadHint.Stay, "bodyhood"),
        P("Hatch inner panel", "Lyukning ichki paneli", "Inner panel of the tailgate.",
            "Orqa lyukning ichki paneli.", SpreadHint.Stay, "rearhatch"),
        P("Rear window", "Orqa oyna", "Glass of the tailgate.", "Orqa lyuk oynasi.", SpreadHint.Dir(0f, 1f, 0f),
            "rearwindow"),
        P("Rear side windows", "Orqa yon oynalar", "Fixed side windows behind the doors.",
            "Eshiklar ortidagi qo‘zg‘almas yon oynalar.", SpreadHint.Dir(0f, 1f, 0f), "windowsrearsides"),
        P("Tail light panels", "Orqa chiroq panellari", "Panels that carry the tail lights.",
            "Orqa chiroqlarni tutuvchi panellar.", SpreadHint.Stay, "taillightspanels"),
        P("Tail lights", "Orqa chiroqlar", "LED tail and brake lights.", "LED orqa va stop chiroqlar.",
            SpreadHint.Dir(0f, 0f, -1f), "taillights"),
        P("Rear turn signals", "Orqa burilish chiroqlari", "Amber turn signal lamps.",
            "Sariq burilish chiroqlari.", SpreadHint.Dir(0f, 1f, -0.5f), "turnsignals"),
        P("Inner rear panels", "Ichki orqa panellar", "Inner panels of the rear section.",
            "Orqa qismning ichki panellari.", SpreadHint.Dir(0f, 0.6f, 1f), "interiorrearpanels"),
        P("Rear panels", "Orqa panellar", "Painted rear skin and bumper.", "Bo‘yalgan orqa qoplama va bamper.",
            SpreadHint.Stay, "rearpanels"),
        P("Licence plate", "Davlat raqami", "Rear licence plate.", "Orqa davlat raqami belgisi.",
            SpreadHint.Dir(0f, 0f, -1f), "license"),
        P("Roof panel", "Tom paneli", "Roof panel of the body shell.", "Kuzovning tom paneli.", SpreadHint.Auto,
            "roof"),
        P("Windscreen seal", "Old oyna zichlagichi", "Rubber seal round the windscreen.",
            "Old oyna atrofidagi rezina zichlagich.", SpreadHint.Dir(0f, 1f, 0f), "windshieldgasket"),
        P("Wiper base", "Tozalagich asosi", "Pivot base that drives the wipers.",
            "Tozalagichlarni harakatlantiruvchi asos.", SpreadHint.Stay, "wipersbase"),
        P("Wiper arms", "Tozalagich richaglari", "Arms and blades that clear the glass.",
            "Oynani tozalovchi richag va cho‘tkalar.", SpreadHint.Dir(0f, 1f, 0.3f), "wipers"),
        P("Windscreen glass", "Old oyna shishasi", "Laminated windscreen glass.", "Ko‘p qatlamli old oyna shishasi.",
            SpreadHint.Stay, "windshield"),
        P("Body pillars", "Kuzov ustunlari", "Pillars that carry the roof.", "Tomni ko‘tarib turuvchi ustunlar.",
            SpreadHint.Stay, "bodypillars"),
        P("Lower body panels", "Pastki kuzov panellari", "Sills and lower panels in the contrast colour.",
            "Kontrast rangdagi ostonalar va pastki panellar.", SpreadHint.Dir(0f, -1f, 0f), "panelscolor2"),
        P("Cabin cage", "Salon karkasi", "Tubular cage that protects the cabin.",
            "Salonni himoya qiluvchi quvurli karkas.", SpreadHint.Dir(0f, 1f, 0f), "interiorcage"),
        P("Inner pillars", "Ichki ustunlar", "Inner pillar trims.", "Ustunlarning ichki qoplamalari.",
            SpreadHint.Dir(0f, 0.5f, 0f), "interiorpillar"),
        P("Seat cushions", "O‘rindiq yostiqlari", "Cushions and backrests of the seats.",
            "O‘rindiqlarning yostiq va suyanchiqlari.", SpreadHint.Stay, "seatscolor1"),
        P("Seat trim", "O‘rindiq qoplamasi", "Contrast trim of the seats.", "O‘rindiqlarning kontrast qoplamasi.",
            SpreadHint.Dir(0f, 1f, 0f), "seatscolor2"),
        P("Seat frame", "O‘rindiq ramasi", "Steel frame of the seats.", "O‘rindiqlarning po‘lat ramasi.",
            SpreadHint.Auto, "seatsframe"),
        P("Centre dashboard", "Markaziy panel", "Centre section of the dashboard.",
            "Asboblar panelining markaziy qismi.", SpreadHint.Stay, "dashmid"),
        P("Dashboard sides", "Panel yon qismlari", "Side sections of the dashboard.",
            "Asboblar panelining yon qismlari.", SpreadHint.Dir(0f, 1f, 0f), "dashsides"),
        P("Column shaft", "Rul ustuni vali", "Shaft that carries the steering wheel.",
            "Rul g‘ildiragini tutuvchi val.", SpreadHint.Dir(0f, 0.3f, -1f), "dashcolumn"),
        P("Instrument binnacle", "Asboblar qutisi", "Housing of the driver's instruments.",
            "Haydovchi asboblari qutisi.", SpreadHint.Dir(0f, 1f, -0.4f), "steeringdash"),
        P("Column shroud", "Ustun qoplamasi", "Shroud round the steering column.", "Rul ustuni qoplamasi.",
            SpreadHint.Stay, "steeringbase"),
        P("Steering emblem", "Rul emblemasi", "Badge in the centre of the wheel.", "Rul markazidagi belgi.",
            SpreadHint.Dir(0f, 0f, -1f), "steeringemblem"),
        P("Rim segment", "Halqa bo‘lagi", "Segment of the steering wheel rim.", "Rul halqasining bo‘lagi.",
            SpreadHint.Auto, "steeringwheel"),
        P("Left grip", "Chap tutqich", "Padded left-hand grip.", "Chap qo‘l uchun yumshoq tutqich.",
            SpreadHint.Auto, "steeringhandlel"),
        P("Right grip", "O‘ng tutqich", "Padded right-hand grip.", "O‘ng qo‘l uchun yumshoq tutqich.",
            SpreadHint.Auto, "steeringhandler"),
        P("Steering hub", "Rul gupchagi", "Hub that joins the wheel to the column.",
            "Rul g‘ildiragini ustunga ulovchi gupchak.", SpreadHint.Stay, "steeringcylinder"),
        P("Pedal arm", "Pedal richagi", "Arm that carries the pedal pad.", "Pedal yostig‘ini tutuvchi richag.",
            SpreadHint.Stay, "accelarm", "brakearm"),
        P("Pedal pad", "Pedal yostig‘i", "Pad the driver's foot presses.", "Haydovchi oyog‘i bosadigan yostiq.",
            SpreadHint.Dir(0f, 0f, -1f), "pedalaccel", "pedalbrake"),
        P("Floor mats", "Pol gilamchalari", "Carpet mats on the cabin floor.", "Salon polidagi gilamchalar.",
            SpreadHint.Dir(0f, 1f, 0f), "floormats"),
        P("Floor pan", "Pol paneli", "Pressed floor of the cabin.", "Salonning shtamplangan poli.",
            SpreadHint.Stay, "interiorfloor"),
        P("Centre console", "Markaziy konsol", "Console between the seats.", "O‘rindiqlar orasidagi konsol.",
            SpreadHint.Dir(0f, 1f, 0f), "interiormid"),
        P("Axles", "O‘qlar", "Front and rear axle beams that locate the wheels.",
            "G‘ildiraklarni joyida tutuvchi old va orqa o‘q to‘sinlari.", SpreadHint.Dir(0f, -1f, 0f), "axles"),
        P("Underbody", "Kuzov tagi", "Floor structure that carries the body and the running gear.",
            "Kuzov va yurish qismini ko‘taruvchi pastki tuzilma.", SpreadHint.Stay, "bodyunderside",
            "carconcept"),
        // V6 (CAD names).
        P("Piston", "Porshen", "Aluminium piston that takes the combustion pressure.",
            "Yonish bosimini qabul qiluvchi alyuminiy porshen.", SpreadHint.Dir(0f, 1f, 0f), "piston head"),
        P("Piston pin", "Porshen barmog‘i", "Pin that joins the piston to its connecting rod.",
            "Porshenni shatunga bog‘lovchi barmoq.", SpreadHint.Dir(1f, 0f, 0f), "piston pin"),
        P("Rod cap", "Shatun qopqog‘i", "Bolted cap that closes the rod round the crankshaft.",
            "Shatunni tirsakli val atrofida yopuvchi boltli qopqoq.", SpreadHint.Dir(0f, -1f, 0f), "piston rod cap"),
        P("Connecting rod", "Shatun", "Rod that links the piston to the crankshaft.",
            "Porshenni tirsakli val bilan bog‘lovchi shatun.", SpreadHint.Stay, "piston rod"),
        P("Piston and rod", "Porshen va shatun", "Piston with its pin, connecting rod and rod cap.",
            "Barmog‘i, shatuni va qopqog‘i bilan porshen.", SpreadHint.Auto, "piston assembly"),
        P("Engine block", "Silindrlar bloki", "Cast block that holds the six cylinders and the crankshaft.",
            "Oltita silindr va tirsakli valni tutuvchi quyma blok.", SpreadHint.Stay, "engine block"),
        P("Cylinder head", "Silindrlar kallagi", "Closes one bank of cylinders and carries its valves.",
            "Bir qator silindrlarni yopib, klapanlarini tutadi.", SpreadHint.Auto, "cylinder head"),
        P("Side cover", "Yon qopqoq", "Cover on the timing side of the engine.",
            "Dvigatelning taqsimlash tomonidagi qopqoq.", SpreadHint.Auto, "side cover"),
        P("Oil pan", "Moy karteri", "Sump that holds the engine oil.", "Dvigatel moyini saqlovchi karter.",
            SpreadHint.Dir(0f, -1f, 0f), "oil pan"),
        P("Crankshaft", "Tirsakli val", "Turns the pistons' up-and-down motion into rotation.",
            "Porshenlarning yuqoriga-pastga harakatini aylanishga aylantiradi.", SpreadHint.Dir(0f, -1f, 0f),
            "crankshaft"),
        P("Camshaft cap", "Val qopqog‘i", "Cap that holds a camshaft in its bearing.",
            "Taqsimlash valini podshipnikda ushlab turuvchi qopqoq.", SpreadHint.Dir(0f, 1f, 0f),
            "cam shaft retainer"),
        P("Camshaft", "Taqsimlash vali", "Shaft that carries the cams.", "Mushtchalarni tutuvchi val.",
            SpreadHint.Stay, "cam shaft"),
        P("Cam lobe", "Mushtcha", "Lobe that lifts a rocker arm once per turn.",
            "Har aylanishda koromisloni ko‘taruvchi mushtcha.", SpreadHint.Auto, "cam"),
        P("Belt pulley", "Tasma shkivi", "Pulley of the accessory belt drive.", "Yordamchi tasma uzatmasi shkivi.",
            SpreadHint.Auto, "belt wheel"),
        P("Valve cover", "Klapan qopqog‘i", "Cover over the valve train of one bank.",
            "Bir qator gaz taqsimlash mexanizmini yopuvchi qopqoq.", SpreadHint.Dir(0f, 1f, 0f), "valve cover"),
        P("Valve spring", "Klapan prujinasi", "Spring that closes a valve.", "Klapanni yopuvchi prujina.",
            SpreadHint.Auto, "backing spring"),
        P("Rocker arm assembly", "Koromislo yig‘masi", "Rocker arm with its pins, rollers and bolt.",
            "O‘qlari, roliklari va bolti bilan koromislo.", SpreadHint.Auto, "rocker arm assembly"),
        P("Rocker pin", "Koromislo o‘qi", "Pin that the rocker arm pivots on.", "Koromislo aylanadigan o‘q.",
            SpreadHint.Auto, "rocker arm pin"),
        P("Rocker roller", "Koromislo roligi", "Roller that follows the cam.", "Mushtcha bo‘ylab yuruvchi rolik.",
            SpreadHint.Auto, "rocker arm roll"),
        P("Hex bolt M8", "Olti qirrali bolt M8", "ISO 8676 fine-thread hex bolt that holds the rocker.",
            "Koromisloni mahkamlovchi ISO 8676 mayda rezbali bolt.", SpreadHint.Dir(0f, 1f, 0f), "hex screw"),
        P("Rocker arm", "Koromislo", "Lever that passes the cam lift to the valve.",
            "Mushtcha harakatini klapanga uzatuvchi richag.", SpreadHint.Stay, "rocker arm"),
        P("Valve", "Klapan", "Poppet valve that opens and closes a port.", "Kanalni ochib-yopuvchi klapan.",
            SpreadHint.Auto, "engine valve"),
        P("Exhaust manifold", "Chiqarish kollektori", "Collects the exhaust gas of one bank.",
            "Bir qator silindrlarning ishlatilgan gazini yig‘adi.", SpreadHint.Auto, "exhaust manifold"),
        P("Intake manifold", "Kiritish kollektori", "Runners that bring the air to the cylinders.",
            "Havoni silindrlarga yetkazuvchi quvurlar.", SpreadHint.Auto, "intake manifold"),
        P("Air filter", "Havo filtri", "Filter housing that cleans the intake air.",
            "Kiruvchi havoni tozalovchi filtr korpusi.", SpreadHint.Auto, "air filter"),
        P("Supercharger", "Nagnetator", "Compressor that pushes more air into the engine.",
            "Dvigatelga ko‘proq havo haydovchi kompressor.", SpreadHint.Auto, "supercharger", "turbo"),
        P("Hose", "Shlang", "Flexible hose of the intake system.", "Kiritish tizimining egiluvchan shlangi.",
            SpreadHint.Auto, "horse", "hose")
    };

    private static readonly string[] AssemblyDescriptionsEnglish =
    {
        "Part of the car.", "Wheel corner: road wheel and disc brake.",
        "Door: shell, window, mirror, handles and inner trim.",
        "Front section: hood, grille, headlights and front underside.",
        "Rear section: panels, tailgate, lights and licence plate.", "Roof panel of the body shell.",
        "Windscreen glass with its seal and wipers.",
        "Body frame: pillars, lower panels, cabin cage and inner pillars.",
        "Seats: cushions, trim and frames.", "Cockpit: dashboard, steering and pedals.",
        "Cabin floor: floor pan, centre console and mats.", "Underbody and the axles that carry the wheels.",
        "Supercharged V6 engine: block, heads, valve train, intake and exhaust."
    };

    private static readonly string[] AssemblyDescriptionsUzbek =
    {
        "Avtomobil qismi.", "G‘ildirak: shina, disk va diskli tormoz.",
        "Eshik: korpus, oyna, ko‘zgu, dastalar va ichki qoplama.",
        "Old qism: kapot, panjara, faralar va pastki panel.",
        "Orqa qism: panellar, orqa lyuk, chiroqlar va raqam belgisi.", "Kuzovning tom paneli.",
        "Zichlagich va tozalagichlari bilan old oyna.",
        "Kuzov karkasi: ustunlar, pastki panellar, salon karkasi va ichki ustunlar.",
        "O‘rindiqlar: yostiqlar, qoplama va ramalar.", "Boshqaruv joyi: asboblar paneli, rul va pedallar.",
        "Salon poli: pol paneli, markaziy konsol va gilamchalar.", "Kuzov tagi va g‘ildiraklarni tutuvchi o‘qlar.",
        "Nagnetatorli V6 dvigatel: blok, kallaklar, gaz taqsimlash, kiritish va chiqarish."
    };

    // ---------------------------------------------------------------- registry

    // Every enabled, independently rendered mesh object under CarConcept is a part; renderers on the same transform
    // form one part. Small CarPartInfo details are left out (and switched off) at the Low quality level.
    private void BuildComponentRegistry()
    {
        HideFineDetail();
        Renderer[] renderers = carRoot.GetComponentsInChildren<Renderer>(true);
        Dictionary<Transform, List<Renderer>> renderersByNode = new Dictionary<Transform, List<Renderer>>();
        List<Transform> order = new List<Transform>();
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                continue;
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
            if ((filter == null || filter.sharedMesh == null) && (skinned == null || skinned.sharedMesh == null))
                continue;
            if (!renderersByNode.TryGetValue(renderer.transform, out List<Renderer> list))
            {
                list = new List<Renderer>();
                renderersByNode.Add(renderer.transform, list);
                order.Add(renderer.transform);
            }
            list.Add(renderer);
        }

        foreach (Transform node in order)
        {
            Renderer[] nodeRenderers = renderersByNode[node].ToArray();
            bool isV6Part = engineRoot != null && (node == engineRoot || node.IsChildOf(engineRoot));
            Part part = new Part
            {
                Target = node,
                DisplayName = isV6Part ? GetReadableEnginePartName(node.name) : GetReadableCarPartName(node.name),
                IsV6Part = isV6Part,
                IsInner = isV6Part || (internalsRoot != null && node.IsChildOf(internalsRoot)),
                Renderers = nodeRenderers,
                Path = GetHierarchyPath(node)
            };
            bool hasLocalBounds = TryGetLocalGeometryBounds(node, nodeRenderers, out part.LocalBounds);

            Collider existing = node.GetComponent<Collider>();
            if (existing != null)
                part.SelectionCollider = existing;
            else
            {
                part.SelectionCollider = hasLocalBounds ? CreateBoundsCollider(node, part.LocalBounds) : null;
                part.OwnsCollider = part.SelectionCollider != null;
            }
            if (part.SelectionCollider != null)
                partByCollider[part.SelectionCollider] = part;
            if (node == carRoot)
                carRootPart = part;
            partByTransform[node] = part;
            parts.Add(part);
        }

        parts.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
    }

    // CarPartInfo.fineDetail: fasteners, clips and thin wires are not drawn at the Low quality level (Quest).
    private void HideFineDetail()
    {
        if (QualitySettings.GetQualityLevel() > 1)
            return;
        foreach (CarPartInfo info in carRoot.GetComponentsInChildren<CarPartInfo>(true))
        {
            if (info == null || !info.fineDetail)
                continue;
            foreach (Renderer renderer in info.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || !renderer.enabled || hiddenFineDetail.Contains(renderer))
                    continue;
                renderer.enabled = false;
                hiddenFineDetail.Add(renderer);
            }
        }
    }

    private void SetFineDetailHidden(bool hidden)
    {
        foreach (Renderer renderer in hiddenFineDetail)
            if (renderer != null)
                renderer.enabled = !hidden;
    }

    // "BodyDoorLHandle01" -> "Left Door Handle": split the CamelCase words, drop the copy numbers and the 'Body'
    // group word, spell out the sides and the run-together words.
    private static string GetReadableCarPartName(string rawName)
    {
        string name = Regex.Replace(rawName.Replace('_', ' ').Trim(), @"(?<!Colou?r)[\s\-]*\d+$", string.Empty);
        if (PartNameOverrides.TryGetValue(name, out string readable))
            return readable;

        bool trim = false;
        Match colour = Regex.Match(name, @"Colou?r(\d+)$");
        if (colour.Success)
        {
            trim = colour.Groups[1].Value != "1";
            name = name.Substring(0, colour.Index);
        }

        // A lone L or R closing a word is the side: "DoorL", "FrontLRim", "HandleL".
        string side = null;
        string sideAfter = null;
        Match sideLetter = Regex.Match(name, @"(?<=[a-z])[LR](?=[A-Z]|$)");
        if (sideLetter.Success)
        {
            side = sideLetter.Value == "L" ? "Left" : "Right";
            MatchCollection before = Regex.Matches(name.Substring(0, sideLetter.Index), @"[A-Z][a-z]*|[a-z]+");
            sideAfter = before.Count > 0 ? before[before.Count - 1].Value : null;
            name = name.Remove(sideLetter.Index, 1);
        }

        List<string> words = new List<string>();
        foreach (Match word in Regex.Matches(name, @"[A-Z]+(?![a-z])|[A-Z][a-z]*|[a-z]+|\d+"))
            words.Add(word.Value);
        if (words.Count > 1 && (words[0] == "Body" || words[0] == "Wheel" ||
                                (words[0] == "Interior" && Array.IndexOf(CabinPartWords, words[1]) >= 0)))
            words.RemoveAt(0);
        if (words.Count > 1 && words[0] == "Pedal")
        {
            words[0] = words[1];
            words[1] = "Pedal";
        }
        if (words.Count > 1 && (words[words.Count - 1] == "Front" || words[words.Count - 1] == "Rear"))
        {
            words.Insert(0, words[words.Count - 1]);
            words.RemoveAt(words.Count - 1);
        }
        if (side != null)
        {
            int at = 0;
            if ((sideAfter == "Front" || sideAfter == "Rear") && words.Remove(sideAfter))
            {
                words.Insert(0, sideAfter);
                at = 1;
            }
            words.Insert(at, side);
        }
        for (int i = 0; i < words.Count; i++)
            if (PartNameWords.TryGetValue(words[i], out string replacement))
                words[i] = replacement;
        if (trim)
            words.Add("Trim");
        return words.Count > 0 ? string.Join(" ", words) : rawName;
    }

    // V6 CAD export names: drop the "^assembly" path, copy counters and file-name noise, fix the typos.
    private static string GetReadableEnginePartName(string rawName)
    {
        string name = rawName.Trim();
        int caret = name.IndexOf('^');
        if (caret > 0)
            name = name.Substring(0, caret);
        name = Regex.Replace(name.Replace('_', ' '), @"(-N\d*|\s*0\d\d)$", string.Empty);
        name = name.Replace("gradeab fine iso ", string.Empty).Replace(" - ", " ")
            .Replace("mirored", "(mirrored)").Replace("Horse", "Hose");
        name = Regex.Replace(Regex.Replace(name, @"([a-z])(\d{1,2})$", "$1 $2"), @"\s+", " ").Trim();
        return name.Length > 0 ? char.ToUpperInvariant(name[0]) + name.Substring(1) : rawName;
    }

    private static string GetHierarchyPath(Transform node)
    {
        if (node == null) return "";
        string path = node.name;
        while (node.parent != null)
        {
            node = node.parent;
            path = node.name + "/" + path;
        }
        return path;
    }

    // ---------------------------------------------------------------- structure tree

    private void BuildStructure()
    {
        carRestBounds = GetWorldBoundsOfParts(parts);
        carRestCenter = carRestBounds.center;
        // The car's own horizontal axes: forward toward the front section (hood) or the V6 bay, right across it.
        Vector3 front = carRestCenter + Vector3.ProjectOnPlane(carRoot.forward, Vector3.up);
        Transform hood = carRoot.Find("BodyHood");
        if (hood != null && partByTransform.TryGetValue(hood, out Part hoodPart))
            front = GetWorldRendererBounds(hoodPart.Renderers).center;
        else if (engineRoot != null && TryGetBounds(engineRoot, out Bounds engineBounds))
            front = engineBounds.center;
        Vector3 forward = Vector3.ProjectOnPlane(front - carRestCenter, Vector3.up);
        carForward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        carRight = Vector3.Cross(Vector3.up, carForward).normalized;

        root = NewNode(null, CarNodeKind.Vehicle, "Aurelle CarConcept", "Aurelle CarConcept",
            "Concept car with a supercharged V6: body, structure, cabin, wheels and powertrain.",
            "Nagnetatorli V6 dvigatelli konsept avtomobil: kuzov, karkas, salon, g‘ildiraklar va kuch agregati.");
        root.Info.Id = "vehicle";

        // Tagged nodes first: they define the tree under them.
        CarPartInfo[] infos = carRoot.GetComponentsInChildren<CarPartInfo>(true);
        foreach (CarPartInfo info in infos)
            if (info != null && info.transform != carRoot)
                taggedByTransform[info.transform] = info;

        foreach (Part part in parts)
        {
            CarPartInfo tagged = FindTaggedAncestor(part.Target);
            Node leaf = tagged != null ? PlaceTaggedPart(part, tagged)
                : part.IsV6Part ? PlaceEnginePart(part)
                : PlaceRulePart(part);
            leaf.Parts.Add(part);
            part.Leaf = leaf;
        }

        // Every node lists the parts under it; empty branches go, single-child generated groups collapse.
        Prune(root);
        // Systems in CarSystems order, assemblies by kind, and inside them the rule groups in table order before the
        // loose parts (the HUD's lists are filled from these by FinishNode).
        root.Children.Sort((a, b) =>
            CarSystems.Order(a.Info.SystemName).CompareTo(CarSystems.Order(b.Info.SystemName)));
        foreach (Node system in root.Children)
            system.Children.Sort(CompareAssemblies);
        SortGroups(root);
        FinishNode(root, 0, null);
        allNodes.Clear();
        CollectNodes(root);
        NumberDuplicates(root);
        AssignIds(root, "");
        foreach (Node node in allNodes)
        {
            node.Info.CanOpen = node.Children.Count >= 2;
            if (node.Info.Kind == CarNodeKind.Assembly)
                assemblies.Add(node);
        }
        foreach (Part part in parts)
        {
            part.Assembly = part.Leaf.Assembly;
            part.IsShell = IsShellPart(part);
        }
        foreach (Node assembly in assemblies)
            SetUpAssembly(assembly);
        BuildTracks();

        int shell = 0;
        foreach (Part part in parts)
            if (part.IsShell) shell++;
        Debug.Log("[Car Explorer] Structure: " + parts.Count + " parts in " + root.Children.Count + " systems, " +
                  assemblies.Count + " assemblies, " + allNodes.Count + " nodes (" + shell + " body shell parts), " +
                  motionTracks.Count + " animated transforms.");
    }

    private CarPartInfo FindTaggedAncestor(Transform node)
    {
        for (; node != null && node != carRoot; node = node.parent)
            if (taggedByTransform.TryGetValue(node, out CarPartInfo info))
                return info;
        return null;
    }

    private Node NewNode(Node parent, CarNodeKind kind, string english, string uzbek, string descriptionEnglish,
        string descriptionUzbek)
    {
        Node node = new Node { Parent = parent, Created = createdNodes++, Order = 1000 };
        node.Info.Kind = kind;
        node.Info.NameEnglish = english;
        node.Info.NameUzbek = string.IsNullOrEmpty(uzbek) ? english : uzbek;
        node.Info.DescriptionEnglish = descriptionEnglish ?? string.Empty;
        node.Info.DescriptionUzbek = string.IsNullOrEmpty(descriptionUzbek) ? descriptionEnglish ?? string.Empty
            : descriptionUzbek;
        node.Info.Material = string.Empty;
        node.Info.SystemName = parent != null ? parent.Info.SystemName : string.Empty;
        if (parent != null)
        {
            parent.Children.Add(node);
            node.Info.Parent = parent.Info;
        }
        return node;
    }

    private Node GetSystem(string english)
    {
        if (string.IsNullOrEmpty(english))
            english = "Body";
        if (systemsByName.TryGetValue(english, out Node system))
            return system;
        int index = Array.IndexOf(CarSystems.English, english);
        system = NewNode(root, CarNodeKind.System, english, CarSystems.ToUzbek(english),
            index >= 0 ? SystemDescriptionsEnglish[index] : english,
            index >= 0 ? SystemDescriptionsUzbek[index] : english);
        system.Info.SystemName = english;
        systemsByName.Add(english, system);
        return system;
    }

    private Node GetKeyed(string key, Func<Node> create)
    {
        if (!nodesByKey.TryGetValue(key, out Node node))
        {
            node = create();
            nodesByKey.Add(key, node);
        }
        return node;
    }

    // ---- tagged content (CarPartInfo)

    private Node GetTaggedNode(CarPartInfo info)
    {
        return GetKeyed("tag:" + info.GetInstanceID(), () =>
        {
            CarPartInfo above = FindTaggedAncestor(info.transform.parent);
            Node parent;
            CarNodeKind kind = info.kind == CarNodeKind.Vehicle || info.kind == CarNodeKind.System
                ? CarNodeKind.SubAssembly : info.kind;
            if (kind == CarNodeKind.Assembly || (above == null && !IsInsideRuleAssembly(info.transform)))
            {
                kind = CarNodeKind.Assembly;
                parent = GetSystem(!string.IsNullOrEmpty(info.system) ? info.system : DefaultSystemFor(info.transform));
            }
            else if (above != null)
                parent = GetTaggedNode(above);
            else if (IsEngineNode(info.transform))
                parent = GetEngineAssembly();
            else
            {
                Node assembly = GetRuleAssembly(info.transform, out AssemblyKind ruleKind);
                parent = GetRuleGroup(assembly, ruleKind, info.name, false);
            }
            string english = !string.IsNullOrEmpty(info.nameEnglish) ? info.nameEnglish
                : IsEngineNode(info.transform) ? GetReadableEnginePartName(info.name)
                : GetReadableCarPartName(info.name);
            Node node = NewNode(parent, kind, english, info.nameUzbek, info.descriptionEnglish, info.descriptionUzbek);
            node.PartInfo = info;
            node.Source = info.transform;
            node.Info.Id = info.id;
            // A tagged V6 keeps the engine's places: forward out of the bay, forward in the exploded view.
            if (kind == CarNodeKind.Assembly && info.transform == engineRoot)
            {
                node.Kind = AssemblyKind.Engine;
                node.Key = "engine";
            }
            node.Info.Material = info.material ?? string.Empty;
            if (!string.IsNullOrEmpty(info.system))
                node.Info.SystemName = info.system;
            else if (kind == CarNodeKind.Assembly)
                node.Info.SystemName = parent.Info.SystemName;
            node.Distance = info.explodeDistance > 0f ? info.explodeDistance : 1f;
            if (info.explodeDirection.sqrMagnitude > 1e-6f)
            {
                Transform space = info.transform.parent != null ? info.transform.parent : carRoot;
                node.Hint = new SpreadHint
                    { Mode = SpreadMode.Direction, Car = space.TransformDirection(info.explodeDirection) };
                node.HintIsWorld = true;
            }
            if (string.IsNullOrEmpty(node.Info.DescriptionEnglish))
                SetTypeDescription(node, info.name);
            return node;
        });
    }

    private bool IsEngineNode(Transform node)
    {
        return engineRoot != null && (node == engineRoot || node.IsChildOf(engineRoot));
    }

    // A tagged node outside every tagged assembly that sits in one of CarConcept's own rule assemblies (a door
    // handle given its own description, say) joins that assembly; anything else is an assembly of its own.
    private bool IsInsideRuleAssembly(Transform node)
    {
        if (IsEngineNode(node))
            return true;
        if (internalsRoot != null && (node == internalsRoot || node.IsChildOf(internalsRoot)))
            return false;
        bool belowTopLevel = node.parent != null && node.parent != carRoot;
        return belowTopLevel || node.GetComponent<Renderer>() != null;
    }

    private string DefaultSystemFor(Transform node)
    {
        if (IsEngineNode(node))
            return "Powertrain";
        if (internalsRoot != null && node.IsChildOf(internalsRoot))
            return "Structure";
        ClassifyTopLevel(node, out AssemblyKind kind, out _);
        return SystemOf(kind);
    }

    private Node PlaceTaggedPart(Part part, CarPartInfo tagged)
    {
        Node owner = GetTaggedNode(tagged);
        if (owner.Info.Kind == CarNodeKind.Part)
            return owner;
        // Untagged content of a tagged group follows its own transform hierarchy.
        return PlaceInHierarchy(owner, tagged.transform, part, part.IsV6Part);
    }

    // Below `owner` (built from `top`), the transforms between `top` and the part become generated sub-assemblies and
    // the part a leaf. A transform that carries a mesh and has children gets its own mesh as a leaf.
    private Node PlaceInHierarchy(Node owner, Transform top, Part part, bool engine)
    {
        Transform target = part.Target;
        if (target == top)
            return GetSelfLeaf(owner, part, engine);
        List<Transform> path = hierarchyScratch;
        path.Clear();
        for (Transform node = target.parent; node != null && node != top; node = node.parent)
            path.Add(node);
        Node parent = owner;
        for (int i = path.Count - 1; i >= 0; i--)
        {
            Transform group = path[i];
            Node above = parent;
            parent = GetKeyed("grp:" + group.GetInstanceID(), () =>
            {
                Node node = NewNode(above, CarNodeKind.SubAssembly, ReadableName(group, engine), null, null, null);
                node.Source = group;
                node.Generated = true;
                SetTypeNames(node, group.name, engine);
                return node;
            });
        }
        if (target.childCount > 0 && HasPartBelow(target))
        {
            Node above = parent;
            parent = GetKeyed("grp:" + target.GetInstanceID(), () =>
            {
                Node node = NewNode(above, CarNodeKind.SubAssembly, ReadableName(target, engine), null, null, null);
                node.Source = target;
                node.Generated = true;
                SetTypeNames(node, target.name, engine);
                return node;
            });
            return GetSelfLeaf(parent, part, engine);
        }
        return NewLeaf(parent, part, engine);
    }

    private bool HasPartBelow(Transform node)
    {
        for (int i = 0; i < node.childCount; i++)
        {
            Transform child = node.GetChild(i);
            if (partByTransform.ContainsKey(child) || HasPartBelow(child))
                return true;
        }
        return false;
    }

    // The mesh of a group node itself (a hub carrying other parts): a leaf named after the group.
    private Node GetSelfLeaf(Node group, Part part, bool engine)
    {
        Node leaf = NewLeaf(group, part, engine);
        if (leaf.Info.NameEnglish == group.Info.NameEnglish)
        {
            leaf.Info.NameEnglish += " (main part)";
            leaf.Info.NameUzbek += " (asosiy qism)";
        }
        return leaf;
    }

    private Node NewLeaf(Node parent, Part part, bool engine)
    {
        Node leaf = NewNode(parent, CarNodeKind.Part, part.DisplayName, null, null, null);
        leaf.Source = part.Target;
        leaf.Generated = true;
        SetTypeNames(leaf, part.Target.name, engine);
        // The pieces of a CAD group named like the group ("Supercharger" in "Supercharger"): its parts.
        if (parent.Generated && parent.Info.NameEnglish == leaf.Info.NameEnglish && parent.Source != part.Target)
        {
            leaf.Info.NameEnglish += " part";
            leaf.Info.NameUzbek += " qismi";
        }
        CarPartInfo info = part.Target.GetComponent<CarPartInfo>();
        if (info != null && !string.IsNullOrEmpty(info.material))
            leaf.Info.Material = info.material;
        return leaf;
    }

    private string ReadableName(Transform node, bool engine)
    {
        return engine ? GetReadableEnginePartName(node.name) : GetReadableCarPartName(node.name);
    }

    // Names, description and spread from the part rules, when one matches the node's raw name.
    private void SetTypeNames(Node node, string rawName, bool engine)
    {
        PartRule rule = FindPartRule(rawName, engine);
        if (rule == null)
        {
            node.Info.DescriptionEnglish = engine ? "Component of the V6 engine." : "Component of the car.";
            node.Info.DescriptionUzbek = engine ? "V6 dvigatelining qismi." : "Avtomobil qismi.";
            return;
        }
        node.Info.NameEnglish = rule.English;
        node.Info.NameUzbek = rule.Uzbek;
        node.Info.DescriptionEnglish = rule.DescriptionEnglish;
        node.Info.DescriptionUzbek = rule.DescriptionUzbek;
        if (!node.HintIsWorld)
            node.Hint = rule.Hint;
    }

    private void SetTypeDescription(Node node, string rawName)
    {
        PartRule rule = FindPartRule(rawName, IsEngineNode(node.Source));
        node.Info.DescriptionEnglish = rule != null ? rule.DescriptionEnglish : "Component of the car.";
        if (string.IsNullOrEmpty(node.Info.DescriptionUzbek))
            node.Info.DescriptionUzbek = rule != null ? rule.DescriptionUzbek : "Avtomobil qismi.";
    }

    private static PartRule FindPartRule(string rawName, bool engine)
    {
        string name = rawName.ToLowerInvariant();
        // The V6's rules come last in the table; CarConcept names never contain spaces, the V6's usually do.
        foreach (PartRule rule in CarPartRules)
        {
            bool engineRule = Array.IndexOf(CarPartRules, rule) >= FirstEnginePartRule;
            if (engineRule != engine)
                continue;
            foreach (string term in rule.Terms)
                if (Matches(name, term))
                    return rule;
        }
        return null;
    }

    // A term matches anywhere in the lower-case name; "=name" matches the whole name only.
    private static bool Matches(string name, string term)
    {
        return term.Length > 0 && term[0] == '=' ? name == term.Substring(1) : name.Contains(term);
    }

    private static readonly int FirstEnginePartRule = Array.FindIndex(CarPartRules, r => r.English == "Piston");

    // ---- the untagged V6

    private Node GetEngineAssembly()
    {
        return GetKeyed("engine", () =>
        {
            Node node = NewNode(GetSystem("Powertrain"), CarNodeKind.Assembly, "V6 Engine", "V6 dvigateli",
                AssemblyDescriptionsEnglish[(int)AssemblyKind.Engine],
                AssemblyDescriptionsUzbek[(int)AssemblyKind.Engine]);
            node.Kind = AssemblyKind.Engine;
            node.Key = "engine";
            node.Source = engineRoot;
            node.Info.Material = "Cast aluminium";
            return node;
        });
    }

    private Node PlaceEnginePart(Part part)
    {
        Node assembly = GetEngineAssembly();
        Transform top = part.Target;
        while (top.parent != null && top.parent != engineRoot)
            top = top.parent;
        if (top == engineRoot)
            return NewLeaf(assembly, part, true);
        Node group = GetRuleGroup(assembly, AssemblyKind.Engine, top.name, true);
        // The CAD hierarchy below the V6 root's child (a piston assembly, a rocker arm assembly) is kept.
        if (top == part.Target)
            return NewLeaf(group, part, true);
        Node sub = GetKeyed("grp:" + top.GetInstanceID(), () =>
        {
            Node node = NewNode(group, CarNodeKind.SubAssembly, GetReadableEnginePartName(top.name), null, null, null);
            node.Source = top;
            node.Generated = true;
            SetTypeNames(node, top.name, true);
            return node;
        });
        return PlaceInHierarchy(sub, top, part, true);
    }

    // ---- CarConcept's own nodes

    private Node PlaceRulePart(Part part)
    {
        Node assembly = GetRuleAssembly(part.Target, out AssemblyKind kind);
        Node group = GetRuleGroup(assembly, kind, part.Target.name, false);
        Node leaf = NewLeaf(group, part, false);
        if (kind == AssemblyKind.Generic)
            leaf.Info.NameEnglish = leaf.Info.NameUzbek = part.DisplayName;
        return leaf;
    }

    private Node GetRuleAssembly(Transform node, out AssemblyKind kind)
    {
        ClassifyTopLevel(node, out kind, out string key);
        AssemblyKind assemblyKind = kind;
        return GetKeyed("asm:" + key, () =>
        {
            string english, uzbek;
            GetAssemblyNames(assemblyKind, key, node, out english, out uzbek);
            Node assembly = NewNode(GetSystem(SystemOf(assemblyKind)), CarNodeKind.Assembly, english, uzbek,
                AssemblyDescriptionsEnglish[(int)assemblyKind], AssemblyDescriptionsUzbek[(int)assemblyKind]);
            assembly.Kind = assemblyKind;
            assembly.Key = key;
            return assembly;
        });
    }

    // The group a part of a rule assembly belongs to (nested groups are created with their parents), or the
    // assembly itself.
    private Node GetRuleGroup(Node assembly, AssemblyKind kind, string rawName, bool engine)
    {
        if (!AssemblyGroups.TryGetValue(kind, out GroupRule[] rules))
            return assembly;
        string name = rawName.ToLowerInvariant();
        GroupRule match = null;
        // The deepest matching group wins: the rim segments' group before the steering wheel's.
        for (int i = rules.Length - 1; i >= 0 && match == null; i--)
            foreach (string term in rules[i].Terms)
                if (Matches(name, term) && !(engine && IsExcludedEngineMatch(rules[i], name)))
                {
                    match = rules[i];
                    break;
                }
        return match != null ? GetGroupNode(assembly, rules, match) : assembly;
    }

    // "cam" also matches the camshaft caps and the camshafts, "piston" the whole piston group: the V6 groups are
    // told apart by their more specific terms first.
    private static bool IsExcludedEngineMatch(GroupRule rule, string name)
    {
        switch (rule.Key)
        {
            case "camshafts": return name.Contains("retainer");
            case "valvetrain": return true;
            case "short": return name.Contains("piston");
            default: return false;
        }
    }

    private Node GetGroupNode(Node assembly, GroupRule[] rules, GroupRule rule)
    {
        Node parent = assembly;
        if (rule.Parent != null)
        {
            GroupRule parentRule = Array.Find(rules, r => r.Key == rule.Parent);
            if (parentRule != null)
                parent = GetGroupNode(assembly, rules, parentRule);
        }
        Node above = parent;
        return GetKeyed("group:" + assembly.Key + "/" + rule.Key, () =>
        {
            Node node = NewNode(above, CarNodeKind.SubAssembly, rule.English, rule.Uzbek, rule.DescriptionEnglish,
                rule.DescriptionUzbek);
            node.Hint = rule.Hint;
            node.Order = Array.IndexOf(rules, rule);
            return node;
        });
    }

    // CarConcept's top-level node names decide the assembly (case-insensitive prefixes).
    private void ClassifyTopLevel(Transform node, out AssemblyKind kind, out string key)
    {
        if (node == carRoot)
        {
            kind = AssemblyKind.Chassis;
            key = "Chassis";
            return;
        }
        for (Transform wheel = node; wheel != null && wheel != carRoot; wheel = wheel.parent)
            if (wheel.name.StartsWith("Wheel", StringComparison.OrdinalIgnoreCase) && wheel.parent == carRoot)
            {
                kind = AssemblyKind.Wheel;
                key = wheel.name;
                return;
            }

        string top = GetTopLevelNode(node).name;
        if (Starts(top, "BodyDoor")) { kind = AssemblyKind.Door; key = top; }
        else if (Starts(top, "BodyHood")) { kind = AssemblyKind.Front; key = "Front"; }
        else if (Starts(top, "BodyRear") || Starts(top, "License")) { kind = AssemblyKind.Rear; key = "Rear"; }
        else if (Starts(top, "BodyRoof")) { kind = AssemblyKind.Roof; key = "Roof"; }
        else if (Starts(top, "BodyWindshield")) { kind = AssemblyKind.Windscreen; key = "Windscreen"; }
        else if (Starts(top, "InteriorSeats")) { kind = AssemblyKind.Seats; key = "Seats"; }
        else if (Starts(top, "InteriorDash") || Starts(top, "InteriorSteering") || Starts(top, "InteriorPedal"))
        { kind = AssemblyKind.Cockpit; key = "Cockpit"; }
        else if (Starts(top, "InteriorFloor") || Starts(top, "InteriorMid")) { kind = AssemblyKind.Floor; key = "Floor"; }
        else if (Starts(top, "Axles") || Starts(top, "Engine")) { kind = AssemblyKind.Chassis; key = "Chassis"; }
        else if (Starts(top, "Body") || Starts(top, "InteriorCage") || Starts(top, "InteriorPillar"))
        { kind = AssemblyKind.BodyFrame; key = "BodyFrame"; }
        else { kind = AssemblyKind.Generic; key = top; }
    }

    private static string SystemOf(AssemblyKind kind)
    {
        switch (kind)
        {
            case AssemblyKind.Wheel: return "Wheels";
            case AssemblyKind.Door: return "Closures";
            case AssemblyKind.Seats:
            case AssemblyKind.Cockpit:
            case AssemblyKind.Floor: return "Interior";
            case AssemblyKind.BodyFrame:
            case AssemblyKind.Chassis: return "Structure";
            case AssemblyKind.Engine: return "Powertrain";
            default: return "Body";
        }
    }

    private void GetAssemblyNames(AssemblyKind kind, string key, Transform node, out string english, out string uzbek)
    {
        Vector3 center = carRestCenter;
        Transform top = node == carRoot ? node : GetTopLevelNode(node);
        if (partByTransform.TryGetValue(top, out Part topPart))
            center = GetWorldRendererBounds(topPart.Renderers).center;
        else if (TryGetBounds(top, out Bounds topBounds))
            center = topBounds.center;
        bool left = Vector3.Dot(center - carRestCenter, carRight) < 0f;
        switch (kind)
        {
            case AssemblyKind.Wheel:
                bool front = Vector3.Dot(center - carRestCenter, carForward) > 0f;
                english = (front ? "Front " : "Rear ") + (left ? "Left" : "Right") + " Wheel";
                uzbek = (front ? "Old " : "Orqa ") + (left ? "chap" : "o‘ng") + " g‘ildirak";
                return;
            case AssemblyKind.Door:
                english = left ? "Left Door" : "Right Door";
                uzbek = left ? "Chap eshik" : "O‘ng eshik";
                return;
            case AssemblyKind.Front: english = "Front Section"; uzbek = "Old qism"; return;
            case AssemblyKind.Rear: english = "Rear Section"; uzbek = "Orqa qism"; return;
            case AssemblyKind.Roof: english = "Roof Panel"; uzbek = "Tom paneli"; return;
            case AssemblyKind.Windscreen: english = "Windscreen"; uzbek = "Old oyna"; return;
            case AssemblyKind.BodyFrame: english = "Body Frame"; uzbek = "Kuzov karkasi"; return;
            case AssemblyKind.Seats: english = "Seats"; uzbek = "O‘rindiqlar"; return;
            case AssemblyKind.Cockpit: english = "Cockpit"; uzbek = "Boshqaruv joyi"; return;
            case AssemblyKind.Floor: english = "Cabin Floor"; uzbek = "Salon poli"; return;
            case AssemblyKind.Chassis: english = "Underbody and Axles"; uzbek = "Kuzov tagi va o‘qlar"; return;
            case AssemblyKind.Engine: english = "V6 Engine"; uzbek = "V6 dvigateli"; return;
            default:
                english = GetReadableCarPartName(key);
                uzbek = english;
                return;
        }
    }

    private Transform GetTopLevelNode(Transform node)
    {
        while (node.parent != null && node.parent != carRoot)
            node = node.parent;
        return node;
    }

    private static bool Starts(string value, string prefix)
    {
        return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    // ---- finishing the tree

    // Removes branches without parts and collapses generated groups (and rule groups) that hold a single child.
    private void Prune(Node node)
    {
        for (int i = node.Children.Count - 1; i >= 0; i--)
        {
            Node child = node.Children[i];
            Prune(child);
            if (child.Children.Count == 0 && child.Parts.Count == 0)
            {
                node.Children.RemoveAt(i);
                continue;
            }
            bool collapsible = child.Info.Kind == CarNodeKind.SubAssembly && child.PartInfo == null &&
                               child.Parts.Count == 0 && child.Children.Count == 1;
            if (collapsible)
            {
                Node only = child.Children[0];
                only.Parent = node;
                only.Info.Parent = node.Info;
                if (only.Hint.Mode == SpreadMode.Auto && !only.HintIsWorld)
                    only.Hint = child.Hint;
                node.Children[i] = only;
            }
        }
        if (node.Info.Kind == CarNodeKind.Assembly && node.PartInfo == null && node.Children.Count == 1 &&
            node.Children[0].Children.Count == 0)
        {
            node.Parts.AddRange(node.Children[0].Parts);
            node.Children.Clear();
        }
    }

    // Parent links, depth, system, part lists and counts, bottom up.
    private void FinishNode(Node node, int depth, Node assembly)
    {
        node.Info.Depth = depth;
        if (node.Info.Kind == CarNodeKind.Assembly)
            assembly = node;
        node.Assembly = assembly;
        node.Info.Children.Clear();
        node.AllParts.Clear();
        node.AllParts.AddRange(node.Parts);
        foreach (Part part in node.Parts)
            part.Leaf = node;
        foreach (Node child in node.Children)
        {
            child.Parent = node;
            child.Info.Parent = node.Info;
            if (node.Info.Kind != CarNodeKind.Vehicle && string.IsNullOrEmpty(child.PartInfo != null
                    ? child.PartInfo.system : null))
                child.Info.SystemName = node.Info.SystemName;
            FinishNode(child, depth + 1, assembly);
            node.Info.Children.Add(child.Info);
            node.AllParts.AddRange(child.AllParts);
        }
        node.Info.PartCount = node.Children.Count == 0 ? (node.AllParts.Count > 0 ? 1 : 0) : 0;
        foreach (Node child in node.Children)
            node.Info.PartCount += child.Info.PartCount;
        node.RestBounds = GetWorldBoundsOfParts(node.AllParts);
        nodeByInfo[node.Info] = node;
    }

    private static void SortGroups(Node node)
    {
        foreach (Node child in node.Children)
            SortGroups(child);
        if (node.Info.Kind == CarNodeKind.Vehicle || node.Info.Kind == CarNodeKind.System)
            return;
        // List.Sort is not stable: the creation order breaks ties.
        node.Children.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : a.Created.CompareTo(b.Created));
    }

    private void CollectNodes(Node node)
    {
        node.Index = allNodes.Count;
        allNodes.Add(node);
        foreach (Node child in node.Children)
            CollectNodes(child);
    }

    // Siblings with the same name are numbered: "Valve 1" ... "Valve 12".
    private static void NumberDuplicates(Node node)
    {
        Dictionary<string, int> counts = new Dictionary<string, int>();
        foreach (Node child in node.Children)
            counts[child.Info.NameEnglish] = counts.TryGetValue(child.Info.NameEnglish, out int n) ? n + 1 : 1;
        Dictionary<string, int> seen = new Dictionary<string, int>();
        foreach (Node child in node.Children)
        {
            string name = child.Info.NameEnglish;
            if (counts[name] > 1)
            {
                int index = seen.TryGetValue(name, out int k) ? k + 1 : 1;
                seen[name] = index;
                child.Info.NameEnglish = name + " " + index;
                child.Info.NameUzbek = child.Info.NameUzbek + " " + index;
            }
            NumberDuplicates(child);
        }
    }

    private void AssignIds(Node node, string prefix)
    {
        if (node.Info.Kind != CarNodeKind.Vehicle)
        {
            string id = node.PartInfo != null && !string.IsNullOrEmpty(node.PartInfo.id) ? node.PartInfo.id
                : (prefix.Length > 0 ? prefix + "." : "") + Slug(node.Info.NameEnglish);
            string unique = id;
            for (int i = 2; usedIds.Contains(unique); i++)
                unique = id + "-" + i;
            usedIds.Add(unique);
            node.Info.Id = unique;
        }
        foreach (Node child in node.Children)
            AssignIds(child, node.Info.Kind == CarNodeKind.Vehicle ? "" : node.Info.Id);
    }

    private static string Slug(string value)
    {
        StringBuilder builder = new StringBuilder(value.Length);
        foreach (char c in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
                builder.Append(c);
            else if (builder.Length > 0 && builder[builder.Length - 1] != '-')
                builder.Append('-');
        }
        return builder.ToString().Trim('-');
    }

    private static readonly AssemblyKind[] AssemblyOrder =
    {
        AssemblyKind.Front, AssemblyKind.Roof, AssemblyKind.Windscreen, AssemblyKind.Rear, AssemblyKind.BodyFrame,
        AssemblyKind.Chassis, AssemblyKind.Door, AssemblyKind.Seats, AssemblyKind.Cockpit, AssemblyKind.Floor,
        AssemblyKind.Wheel, AssemblyKind.Engine, AssemblyKind.Generic
    };

    private static int CompareAssemblies(Node a, Node b)
    {
        int order = Array.IndexOf(AssemblyOrder, a.Kind).CompareTo(Array.IndexOf(AssemblyOrder, b.Kind));
        if (a.PartInfo != null || b.PartInfo != null)
            order = (a.PartInfo != null ? 1 : 0).CompareTo(b.PartInfo != null ? 1 : 0);
        return order != 0 ? order : string.CompareOrdinal(a.Info.NameEnglish, b.Info.NameEnglish);
    }

    // The body skin: everything under a Body* (or licence plate) node, door and hatch trims included, plus, for other
    // models, anything named like a panel or a window. With the generated internals, the floor tub (the underbody on
    // the car's root and the cabin floor pan) closes the wheel wells round the suspension and the cabin trims (the
    // dash, the instrument binnacle and column shroud, the centre console, the cage and the inner pillars) cover the
    // bulkhead systems, the cross-car beam, the seat frames and the steering column, so they turn to faint glass too.
    // The seats, the steering wheel, the pedals, the wheels, the engine and the internals stay solid.
    private bool IsShellPart(Part part)
    {
        if (part.IsInner)
            return false;
        if (part.Target == carRoot)
            return internalsRoot != null;
        for (Transform wheel = part.Target; wheel != null && wheel != carRoot; wheel = wheel.parent)
            if (wheel.name.StartsWith("Wheel", StringComparison.OrdinalIgnoreCase))
                return false;
        string top = GetTopLevelNode(part.Target).name;
        if (Starts(top, "Body") || Starts(top, "License") ||
            (internalsRoot != null && (Starts(top, "InteriorFloor") || IsCabinTrim(top))))
            return true;
        if (Starts(top, "Interior") || Starts(top, "Axle") || Starts(top, "Engine") ||
            (part.Assembly != null && part.Assembly.PartInfo != null))
            return false;
        return ContainsAny(part.Target.name, "Window", "Glass", "Windshield", "Door", "Hood", "Bonnet", "Bumper",
            "Fender", "Roof", "Mirror", "Trunk", "Panel");
    }

    // CarConcept's cabin trims by top-level node (all named Interior*, so their glass is the faint inner-shell kind):
    // InteriorDashMid and InteriorDashSides, InteriorSteeringDash and InteriorSteeringDashColumn, InteriorMid,
    // InteriorCage, InteriorPillar. Not the seats, the steering wheel (InteriorSteeringBase, -Cylinder, -Handle*) or
    // the pedals.
    private static bool IsCabinTrim(string top)
    {
        return Starts(top, "InteriorDash") || Starts(top, "InteriorSteeringDash") || Starts(top, "InteriorMid") ||
               Starts(top, "InteriorCage") || Starts(top, "InteriorPillar");
    }

    // Layers inside the skin: door and hatch trims, the inner hood and its underside, window seals, wiper bases.
    private static bool IsInnerShell(string name)
    {
        return ContainsAny(name, "Interior", "Under", "Gasket", "Base");
    }
}
