using BepInEx;
using RoR2;
using R2API;
using RoR2BepInExPack.GameAssetPaths;
using UnityEngine;
using UnityEngine.AddressableAssets;
using BepInEx.Configuration;

namespace ExamplePlugin;

// Here are some basic resources on code style and naming conventions to help
// you in your first CSharp plugin!
// https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions
// https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/identifier-names
// https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/names-of-namespaces

// To demonstrate some basics, we will be creating an item. Let's say

[BepInAutoPlugin]
public partial class Plugin : BaseUnityPlugin
{
    // The definition of our item will need to persist, so we will make it a
    // static member of our plugin.
    public static ItemDef item;

    // Our item has a few values associated with it - the chance to activate,
    // how long the invis should last - so we will create some configuration
    // values for it. These will be configurable by the users of our mod, so
    // they can change them to whatever they want.
    public ConfigEntry<float> OnKillChance;
    // The first stack of an item sometimes provides a different value than
    // subsequent stacks - we can reflect that behavior here, and allow the two
    // to be modified separately.
    public ConfigEntry<float> InvisDurationBase;
    public ConfigEntry<float> InvisDurationPerStack;

    private void Awake()
    {
        // BepInEx gives us a logger which we can use to log information.
        Log.Init(Logger);

        // Log our awake here so we can see it in LogOutput.log file
        Log.Info($"Plugin {Name} is loaded!");

        // Let's define our item. This, as the name implies, creates an instance
        // of the ItemDef class.
        item = ScriptableObject.CreateInstance<ItemDef>();

        // Language tokens make it so that we can support multiple languages at
        // once.
        // https://risk-of-thunder.github.io/R2Wiki/Mod-Creation/Assets/Localization/
        item.name = "EXAMPLE_CLOAKONKILL_NAME";
        item.nameToken = "EXAMPLE_CLOAKONKILL_NAME";
        item.pickupToken = "EXAMPLE_CLOAKONKILL_PICKUP";
        item.descriptionToken = "EXAMPLE_CLOAKONKILL_DESC";
        item.loreToken = "EXAMPLE_CLOAKONKILL_LORE";

        // Tier determines the rarity of the item.
        // Tier1 = white, Tier2 = green, Tier3 = red, Lunar = lunar, Boss = yellow
        item.tier = ItemTier.Tier2;

        // You are able to create custom icons and models for your item that the
        // game will use when displaying them. This is commonly done through
        // AssetBundles. However, for our purposes, and to keep this example
        // very simple, we will use the default question marks the game provides.
        item.pickupIconSprite = Addressables.LoadAssetAsync<Sprite>(RoR2_Base_Common_MiscIcons.texMysteryIcon_png).WaitForCompletion();
        item.pickupModelReference = new AssetReferenceGameObject(RoR2_Base_Mystery.PickupMystery_prefab);

        // This determines whether a printer or shrine of order can take this
        // item - set to false for broken versions of items, for example.
        item.canRemove = true;

        // This determines whether the item will get a pickup notification, and
        // whether it appears in the inventory at the top of the screen - set to
        // true for hidden helper items, like the secret one you get when playing
        // on Drizzle, or the one given to ghosts from Happiest Mask.
        item.hidden = false;

        // Tags determine a few things, such as what category chest the item can
        // be found in (Utility, Damage, and Healing), or whether the item can
        // drop from chests at all (WorldUnique).
        item.tags = [ItemTag.Utility];

        // Display rules dictate how an item's display model should appear on
        // each survivor. They're a bit tedious to make, and we don't even have
        // a model right now, so we are just going to make them empty.
        // When you do make them, you should use a tool like KingEnderBrine's
        // ItemDisplayPlacementHelper.
        var displays = new ItemDisplayRuleDict(null);

        // This call will register our content to the game. We have to do this
        // before the game is fully loaded, so we have to do it in our Awake.
        ItemAPI.Add(new CustomItem(item, displays));

        // We will also set up our item's settings now, like so:
        OnKillChance = Config.Bind(
            // This will group our settings together under a single section - in
            // our case, all of the settings for a given item.
            "Item: " + item.nameToken,
            // The name of the setting.
            "Proc Chance",
            // The default value of the setting - will be overriden by whatever
            // is in the file at runtime.
            50f,
            // A description, to assist users in knowing what a setting will do.
            "Chance on kill for the invisibility to proc."
        );
        InvisDurationBase = Config.Bind(
            "Item: " + item.nameToken,
            "Invisibility Duration Base",
            3f,
            "How long should the invisibility last with the first stack?"
        );
        InvisDurationPerStack = Config.Bind(
            "Item: " + item.nameToken,
            "Invisibility Duration Per Stack",
            1f,
            "How much longer should the invisibility last per additional stack?"
        );

        // We'll also need to set up the base values for our language tokens.
        // These will be selected when no language is specified.
        // https://risk-of-thunder.github.io/R2Wiki/Mod-Creation/Developer-Reference/Style-Reference-Sheet/
        LanguageAPI.Add(item.nameToken, "Cloak on Kill");
        LanguageAPI.Add(item.pickupToken, "Chance on kill to turn invisible for a short time.");
        LanguageAPI.Add(item.descriptionToken, $"<style=cIsUtility>{OnKillChance.Value}%</style> chance on kill to turn <style=cIsUtility>invisible</style> for <style=cIsUtility>{InvisDurationBase.Value} seconds</style> <style=cStack>(+{InvisDurationPerStack.Value} seconds per stack)</style>.");
        LanguageAPI.Add(item.loreToken, "What am I, an <style=cDeath>English major</style>?");

        // Now that we've added our item, we need to make it do something. For
        // that, we will need to "hook" one of the game's functions - in our
        // case, we want to know when the owner of the item kills an enemy.
        // https://risk-of-thunder.github.io/R2Wiki/Mod-Creation/C%23-Programming/Hooking/
        // We can achieve this like so:
        GlobalEventManager.onCharacterDeathGlobal += OnCharacterDeathGlobal;
    }

    // We make a new function that will be called every time the vanilla function
    // is called. This allows us to respond to things happening in the game with
    // custom code.
    private void OnCharacterDeathGlobal(DamageReport report)
    {
        // Note, this function is for every death that occurs - so to start, we
        // need to bail early sometimes.

        // Killed by the world? (fall damage, etc.) Nope.
        if (!report.attacker || !report.attackerBody)
        {
            return;
        }

        // Don't have an inventory? Nope.
        if (!report.attackerBody.inventory)
        {
            return;
        }

        // We can check to see if this attacker has any of our item in their
        // inventory like so:
        int count = report.attackerBody.inventory.GetItemCount(item.itemIndex);
        if (count > 0)
        {
            // This general pattern will work for most items. Now, let's say our
            // item has a 50% chance to work - here's how you can check:
            if (Util.CheckRoll(50f, report.attackerMaster))
            {
                // CheckRoll will automatically account for positive and negative
                // luck, so we don't need to worry about it. You can also use
                // "CheckRoll0To1" if your chance is between 0 and 1.

                // Since it succeeded, we now need to actually do what the item
                // is supposed to. In our case, we want to turn invisible for a
                // short time. We can use the vanilla Cloak buff to do that, and
                // add it like so:
                report.attackerBody.AddTimedBuff(RoR2Content.Buffs.Cloak, 3f + count);
            }
        }
    }

    // FixedUpdate is where you want to do any game logic that must be redone
    // constantly.
    private void FixedUpdate()
    {
        // Unity's input system lets us easily check for key presses - in our
        // case, lets check for the F2 key being released. For things like skills
        // this is handled by the game, and does not need to be done.
        if (Input.GetKeyUp(KeyCode.F2))
        {
            // We want to create a pickup droplet that contains our item, so we
            // will need the position of the player.
            var transform = PlayerCharacterMasterController.instances[0].master.GetBodyObject().transform;

            // Then, using that position, we spawn our item in front of the
            // player, like so:
            PickupDropletController.CreatePickupDroplet(
                PickupCatalog.FindPickupIndex(item.itemIndex), 
                transform.position, 
                transform.forward * 20f
            );

            // Let's log that it occurred, for good measure.
            Log.Debug($"Player pressed F2 - spawning item at {transform.position}.");
        }
    }
}