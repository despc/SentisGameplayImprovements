import pathlib
import re
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]
CONFIG = ROOT / "SentisGameplayImprovements" / "Config" / "MainConfig.cs"
PATCH = ROOT / "SentisGameplayImprovements" / "Tweaks" / "ShipToolRadiusPatch.cs"
PLUGIN = ROOT / "SentisGameplayImprovements" / "SentisGameplayImprovementsPlugin.cs"
COMMANDS = ROOT / "SentisGameplayImprovements" / "Commands" / "ShipToolRadiusCommands.cs"


class ShipToolRadiusContractTests(unittest.TestCase):
    def test_three_live_multiplier_settings_default_to_one(self):
        source = CONFIG.read_text(encoding="utf-8")
        for tool in ("Welder", "Grinder", "Drill"):
            self.assertRegex(source, rf"_{tool[0].lower() + tool[1:]}RadiusMultiplier\s*=\s*1f")
            self.assertRegex(source, rf"public\s+float\s+{tool}RadiusMultiplier")
        self.assertGreaterEqual(source.count("ShipToolRadiusPatch.ApplyAllAsync()"), 3)
        self.assertGreaterEqual(source.count('GroupName = "Ship tools"'), 3)

    def test_patch_applies_to_new_and_existing_blocks_without_restart(self):
        source = PATCH.read_text(encoding="utf-8")
        self.assertIn("[PatchShim]", source)
        self.assertIn('typeof(MyShipToolBase).GetMethod("OnAddedToScene"', source)
        self.assertIn('typeof(MyShipDrill).GetMethod("Init"', source)
        self.assertRegex(source, r"AfterShipToolAddedToScene[\s\S]*?__instance is MyShipWelder \|\| __instance is MyShipGrinder")
        self.assertIn("ApplyAllAsync", source)
        self.assertIn("EntitiesObserver.MyCubeGrids", source)
        self.assertIn("GetFatBlocks()", source)
        self.assertIn("ApplyToBlock", source)
        self.assertIn("definition.SensorRadius * multiplier", source)
        self.assertRegex(source, r'GetField\(\s*"m_detectorSphere"')
        self.assertIn("ShipToolRadiusPatch.ApplyAllAsync();", PLUGIN.read_text(encoding="utf-8"))

    def test_drill_changes_detection_and_real_voxel_cutout_radius(self):
        source = PATCH.read_text(encoding="utf-8")
        self.assertIn("DrillRadiusMultiplier", source)
        self.assertIn('GetField("m_radius"', source)
        self.assertIn('GetField(\n            "Radius"', source)
        self.assertIn('GetField(\n            "m_sphere"', source)
        self.assertNotIn('GetMethod("GetDrillingSphere"', source)
        self.assertIn("definition.CutOutRadius * multiplier", source)
        self.assertIn("UnRegisterDrill", source)
        self.assertIn("RegisterDrill", source)
        self.assertIn("OnWorldPositionChanged", source)

    def test_runtime_admin_commands_and_immediate_persistence_exist(self):
        source = COMMANDS.read_text(encoding="utf-8")
        for tool in ("welder", "grinder", "drill"):
            self.assertIn(f'[Command("toolradius {tool}"', source)
        self.assertGreaterEqual(source.count("[Permission(MyPromoteLevel.Admin)]"), 4)
        self.assertGreaterEqual(source.count("SaveConfig()"), 3)
        self.assertIn('[Command("toolradius"', source)


if __name__ == "__main__":
    unittest.main()
