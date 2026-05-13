"""Rebuild gameplay.tscn HUD subtree — anchors follow viewport; fractional positions derived from UI.tscn 1920×1080 refs."""

from pathlib import Path

HUD_START_MARKER = '[node name="UILayoutRef"'
END_MARKER = '[node name="EndTurnBtn"'

# Paths match gameplay ext_resource ids 5..29 plus Physic/Magic (42_phys, 43_mag).

HUD_BODY = """
[node name="UILayoutRef" type="TextureRect" parent="UICanvas/HUD"]
visible = false
layout_mode = 1
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
grow_horizontal = 2
grow_vertical = 2
mouse_filter = 2
modulate = Color(1, 1, 1, 0.35686275)
texture = ExtResource("5")
expand_mode = 1
stretch_mode = 5

[node name="PlayerInfo" type="Control" parent="UICanvas/HUD"]
layout_mode = 1
anchors_preset = 3
anchor_left = 0.0
anchor_top = 1.0
anchor_right = 0.0
anchor_bottom = 1.0
offset_left = 17.0
offset_top = -244.0
offset_right = 667.0
offset_bottom = 0.0
grow_horizontal = 2
grow_vertical = 2
mouse_filter = 2

[node name="HpRow" type="Control" parent="UICanvas/HUD/PlayerInfo"]
layout_mode = 1
anchors_preset = 0
offset_left = 353.0
offset_top = 126.0
offset_right = 650.0
offset_bottom = 162.0

[node name="HpBg" type="TextureRect" parent="UICanvas/HUD/PlayerInfo/HpRow"]
layout_mode = 1
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
offset_left = 3.0
offset_top = 3.0
offset_right = -3.0
offset_bottom = -3.0
grow_horizontal = 2
grow_vertical = 2
mouse_filter = 2
texture = ExtResource("6")
expand_mode = 1
stretch_mode = 5

[node name="HpFill" type="TextureProgressBar" parent="UICanvas/HUD/PlayerInfo/HpRow"]
unique_name_in_owner = true
layout_mode = 1
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
offset_left = 3.0
offset_top = 3.0
offset_right = -3.0
offset_bottom = -3.0
grow_horizontal = 2
grow_vertical = 2
mouse_filter = 2
max_value = 100.0
show_percentage = false
nine_patch_stretch = false
texture_progress = ExtResource("7")

[node name="HpFrame" type="TextureRect" parent="UICanvas/HUD/PlayerInfo/HpRow"]
layout_mode = 1
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
grow_horizontal = 2
grow_vertical = 2
mouse_filter = 2
texture = ExtResource("8")
expand_mode = 1
stretch_mode = 5

[node name="HpLabel" type="Label" parent="UICanvas/HUD/PlayerInfo/HpRow"]
unique_name_in_owner = true
layout_mode = 1
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
offset_left = 8.0
grow_horizontal = 2
grow_vertical = 2
mouse_filter = 2
theme_override_colors/font_outline_color = Color(0, 0, 0, 1)
theme_override_constants/outline_size = 4
theme_override_font_sizes/font_size = 15
text = "100/100"
vertical_alignment = 1

[node name="MpRow" type="Control" parent="UICanvas/HUD/PlayerInfo"]
layout_mode = 1
anchors_preset = 0
offset_left = 353.0
offset_top = 172.0
offset_right = 650.0
offset_bottom = 208.0

[node name="MpBg" type="TextureRect" parent="UICanvas/HUD/PlayerInfo/MpRow"]
layout_mode = 1
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
offset_left = 3.0
offset_top = 3.0
offset_right = -3.0
offset_bottom = -3.0
grow_horizontal = 2
grow_vertical = 2
mouse_filter = 2
texture = ExtResource("6")
expand_mode = 1
stretch_mode = 5

[node name="EnergyFill" type="TextureProgressBar" parent="UICanvas/HUD/PlayerInfo/MpRow"]
unique_name_in_owner = true
layout_mode = 1
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
offset_left = 3.0
offset_top = 3.0
offset_right = -3.0
offset_bottom = -3.0
grow_horizontal = 2
grow_vertical = 2
mouse_filter = 2
max_value = 100.0
show_percentage = false
nine_patch_stretch = false
texture_progress = ExtResource("9")

[node name="MpFrame" type="TextureRect" parent="UICanvas/HUD/PlayerInfo/MpRow"]
layout_mode = 1
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
grow_horizontal = 2
grow_vertical = 2
mouse_filter = 2
texture = ExtResource("8")
expand_mode = 1
stretch_mode = 5

[node name="EnergyLabel" type="Label" parent="UICanvas/HUD/PlayerInfo/MpRow"]
unique_name_in_owner = true
layout_mode = 1
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
offset_left = 8.0
grow_horizontal = 2
grow_vertical = 2
mouse_filter = 2
theme_override_colors/font_outline_color = Color(0, 0, 0, 1)
theme_override_constants/outline_size = 4
theme_override_font_sizes/font_size = 15
text = "100/100"
vertical_alignment = 1

[node name="PortraitFrame" type="TextureRect" parent="UICanvas/HUD/PlayerInfo"]
layout_mode = 1
anchors_preset = 0
offset_left = 11.5
offset_top = 16.5
offset_right = 182.5
offset_bottom = 187.5
mouse_filter = 2
texture = ExtResource("12")
expand_mode = 1
stretch_mode = 5

[node name="Portrait" type="TextureRect" parent="UICanvas/HUD/PlayerInfo/PortraitFrame"]
unique_name_in_owner = true
layout_mode = 1
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
offset_left = 12.0
offset_top = 12.0
offset_right = -12.0
offset_bottom = -12.0
grow_horizontal = 2
grow_vertical = 2
mouse_filter = 2
expand_mode = 1
stretch_mode = 5

[node name="StrengthAttribute" type="Sprite2D" parent="UICanvas/HUD/PlayerInfo"]
position = Vector2(32, 171)
scale = Vector2(0.5, 0.5)
texture = ExtResource("42_phys")

[node name="MagicAttribute" type="Sprite2D" parent="UICanvas/HUD/PlayerInfo"]
position = Vector2(160, 167)
scale = Vector2(0.5, 0.5)
texture = ExtResource("43_mag")

[node name="StrLabel" type="Label" parent="UICanvas/HUD/PlayerInfo"]
unique_name_in_owner = true
layout_mode = 1
anchors_preset = 0
offset_left = 4.0
offset_top = 146.0
offset_right = 60.0
offset_bottom = 198.0
theme_override_colors/font_color = Color(0.290196, 0.164706, 0.0627451, 1)
theme_override_colors/font_outline_color = Color(0, 0, 0, 1)
theme_override_constants/outline_size = 4
theme_override_font_sizes/font_size = 32
text = "6"
horizontal_alignment = 1
vertical_alignment = 1

[node name="MagLabel" type="Label" parent="UICanvas/HUD/PlayerInfo"]
unique_name_in_owner = true
layout_mode = 1
anchors_preset = 0
offset_left = 132.0
offset_top = 142.0
offset_right = 188.0
offset_bottom = 194.0
theme_override_colors/font_color = Color(0.368627, 0.00392157, 0.156863, 1)
theme_override_colors/font_outline_color = Color(0, 0, 0, 1)
theme_override_constants/outline_size = 4
theme_override_font_sizes/font_size = 32
text = "6"
horizontal_alignment = 1
vertical_alignment = 1

[node name="ActiveSkillSlot" type="Control" parent="UICanvas/HUD"]
layout_mode = 1
anchors_preset = 7
anchor_left = 0.5
anchor_top = 1.0
anchor_right = 0.5
anchor_bottom = 1.0
offset_left = -508.0
offset_top = -264.0
offset_right = 508.0
offset_bottom = -13.0
grow_horizontal = 2
grow_vertical = 2
mouse_filter = 2

[node name="SkillBarDecor" type="TextureRect" parent="UICanvas/HUD/ActiveSkillSlot"]
layout_mode = 1
anchors_preset = 0
offset_left = 49.5
offset_top = 163.5
offset_right = 1032.5
offset_bottom = 232.5
mouse_filter = 2
texture = ExtResource("14")
expand_mode = 1
stretch_mode = 5

[node name="SkillArea" type="Control" parent="UICanvas/HUD/ActiveSkillSlot"]
layout_mode = 1
anchors_preset = 5
anchor_left = 0.5
anchor_right = 0.5
grow_horizontal = 2
grow_vertical = 2
mouse_filter = 2
offset_left = -416.5
offset_right = 416.5
offset_bottom = 214.0

[node name="SingleSkill1" type="Control" parent="UICanvas/HUD/ActiveSkillSlot/SkillArea"]
layout_mode = 1
anchors_preset = 0
offset_left = -1.0
offset_top = 26.0
offset_right = 147.0
offset_bottom = 204.0

[node name="SkillIcon" type="TextureRect" parent="UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill1"]
layout_mode = 1
anchors_preset = 0
offset_left = 19.5
offset_top = 21.5
offset_right = 126.5
offset_bottom = 128.5
mouse_filter = 2
texture = ExtResource("15")
expand_mode = 1
stretch_mode = 5

[node name="SkillCdCover" type="TextureRect" parent="UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill1"]
layout_mode = 1
anchors_preset = 0
offset_left = 20.5
offset_top = 21.5
offset_right = 127.5
offset_bottom = 128.5
mouse_filter = 2
texture = ExtResource("16")
expand_mode = 1
stretch_mode = 5

[node name="Frame" type="TextureRect" parent="UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill1"]
layout_mode = 1
anchors_preset = 0
offset_left = 13.5
offset_top = 14.5
offset_right = 134.5
offset_bottom = 135.5
mouse_filter = 2
texture = ExtResource("17")
expand_mode = 1
stretch_mode = 5

[node name="SkillHi" type="TextureRect" parent="UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill1"]
layout_mode = 1
anchors_preset = 0
offset_left = -1.5
offset_top = -1.5
offset_right = 149.5
offset_bottom = 150.5
mouse_filter = 2
texture = ExtResource("18")
expand_mode = 1
stretch_mode = 5

[node name="SkillQtyStrip" type="TextureRect" parent="UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill1"]
layout_mode = 1
anchors_preset = 0
offset_left = 50.5
offset_top = 129.5
offset_right = 93.5
offset_bottom = 172.5
mouse_filter = 2
texture = ExtResource("19")
expand_mode = 1
stretch_mode = 5

[node name="QtyLabel" type="Label" parent="UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill1"]
layout_mode = 1
anchors_preset = 0
offset_left = 53.5
offset_top = 139.5
offset_right = 92.5
offset_bottom = 164.5
theme_override_constants/outline_size = 4
theme_override_font_sizes/font_size = 13
theme_override_colors/font_outline_color = Color(0.05, 0.03, 0.08, 1)
text = "1"
horizontal_alignment = 1

[node name="CdLabel" type="Label" parent="UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill1"]
layout_mode = 1
anchors_preset = 0
offset_left = 26.5
offset_top = 50.5
offset_right = 118.5
offset_bottom = 98.5
theme_override_colors/font_outline_color = Color(0, 0, 0, 1)
theme_override_constants/outline_size = 6
theme_override_font_sizes/font_size = 22
text = "12"
horizontal_alignment = 1
vertical_alignment = 1

[node name="SingleSkill2" type="Control" parent="UICanvas/HUD/ActiveSkillSlot/SkillArea"]
layout_mode = 1
anchors_preset = 0
offset_left = 146.0
offset_top = 26.0
offset_right = 294.0
offset_bottom = 204.0

[node name="SkillIcon" type="TextureRect" parent="UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill2"]
layout_mode = 1
anchors_preset = 0
offset_left = 19.5
offset_top = 21.5
offset_right = 126.5
offset_bottom = 128.5
mouse_filter = 2
texture = ExtResource("15")
expand_mode = 1
stretch_mode = 5

[node name="SkillCdCover" type="TextureRect" parent="UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill2"]
layout_mode = 1
anchors_preset = 0
offset_left = 20.5
offset_top = 21.5
offset_right = 127.5
offset_bottom = 128.5
mouse_filter = 2
texture = ExtResource("16")
expand_mode = 1
stretch_mode = 5

[node name="Frame" type="TextureRect" parent="UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill2"]
layout_mode = 1
anchors_preset = 0
offset_left = 13.5
offset_top = 14.5
offset_right = 134.5
offset_bottom = 135.5
mouse_filter = 2
texture = ExtResource("17")
expand_mode = 1
stretch_mode = 5

[node name="SkillHi" type="TextureRect" parent="UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill2"]
layout_mode = 1
anchors_preset = 0
offset_left = -1.5
offset_top = -1.5
offset_right = 149.5
offset_bottom = 150.5
mouse_filter = 2
texture = ExtResource("18")
expand_mode = 1
stretch_mode = 5

[node name="SkillQtyStrip" type="TextureRect" parent="UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill2"]
layout_mode = 1
anchors_preset = 0
offset_left = 196.5
offset_top = 129.5
offset_right = 239.5
offset_bottom = 172.5
mouse_filter = 2
texture = ExtResource("19")
expand_mode = 1
stretch_mode = 5

[node name="QtyLabel" type="Label" parent="UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill2"]
layout_mode = 1
anchors_preset = 0
offset_left = 199.5
offset_top = 139.5
offset_right = 238.5
offset_bottom = 164.5
theme_override_constants/outline_size = 4
theme_override_font_sizes/font_size = 13
theme_override_colors/font_outline_color = Color(0.05, 0.03, 0.08, 1)
text = "1"
horizontal_alignment = 1

[node name="CdLabel" type="Label" parent="UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill2"]
layout_mode = 1
anchors_preset = 0
offset_left = 173.5
offset_top = 50.5
offset_right = 265.5
offset_bottom = 98.5
theme_override_colors/font_outline_color = Color(0, 0, 0, 1)
theme_override_constants/outline_size = 6
theme_override_font_sizes/font_size = 22
text = "12"
horizontal_alignment = 1
vertical_alignment = 1

[node name="PassiveSkillSlot" type="Control" parent="UICanvas/HUD"]
layout_mode = 1
anchors_preset = 0
anchor_left = 0.0
anchor_top = 0.43796296
anchor_right = 0.0
anchor_bottom = 0.43796296
offset_left = 12.0
offset_top = -334.0
offset_right = 222.0
offset_bottom = 334.0
grow_horizontal = 2
grow_vertical = 2
mouse_filter = 2

[node name="PassiveSlots" type="TextureRect" parent="UICanvas/HUD/PassiveSkillSlot"]
layout_mode = 1
anchors_preset = 0
offset_left = -12.5
offset_top = 22.0
offset_right = 164.5
offset_bottom = 648.0
mouse_filter = 2
texture = ExtResource("20")
expand_mode = 1
stretch_mode = 5

[node name="P1" type="TextureRect" parent="UICanvas/HUD/PassiveSkillSlot"]
layout_mode = 1
anchors_preset = 0
offset_left = 8.5
offset_top = 49.0
offset_right = 64.5
offset_bottom = 105.0
mouse_filter = 2
texture = ExtResource("21")
expand_mode = 1
stretch_mode = 5

[node name="P2" type="TextureRect" parent="UICanvas/HUD/PassiveSkillSlot"]
layout_mode = 1
anchors_preset = 0
offset_left = 77.5
offset_top = 49.0
offset_right = 133.5
offset_bottom = 105.0
mouse_filter = 2
texture = ExtResource("21")
expand_mode = 1
stretch_mode = 5

[node name="P3" type="TextureRect" parent="UICanvas/HUD/PassiveSkillSlot"]
layout_mode = 1
anchors_preset = 0
offset_left = 8.5
offset_top = 114.75
offset_right = 64.5
offset_bottom = 170.75
mouse_filter = 2
texture = ExtResource("21")
expand_mode = 1
stretch_mode = 5

[node name="BossSkillArea" type="Control" parent="UICanvas/HUD"]
layout_mode = 1
anchors_preset = 0
anchor_left = 1.0
anchor_top = 0.62129629
anchor_right = 1.0
anchor_bottom = 0.62129629
offset_left = -216.0
offset_top = -253.0
offset_right = -9.0
offset_bottom = 253.0
grow_horizontal = 2
grow_vertical = 2
mouse_filter = 2

[node name="BossPhaseLabel" type="Label" parent="UICanvas/HUD/BossSkillArea"]
unique_name_in_owner = true
layout_mode = 1
anchors_preset = 10
anchor_right = 1.0
offset_top = -3.925
offset_bottom = 42.975
grow_horizontal = 2
theme_override_colors/font_outline_color = Color(0.05, 0.02, 0.06, 1)
theme_override_constants/outline_size = 4
theme_override_font_sizes/font_size = 13
text = "BOSS"
horizontal_alignment = 1

[node name="BossMeterWrap" type="Control" parent="UICanvas/HUD/BossSkillArea"]
layout_mode = 1
anchors_preset = 0
offset_left = 13.975
offset_top = 6.975
offset_right = 42.975
offset_bottom = 499.975
mouse_filter = 2

[node name="BossSkillTrack" type="TextureRect" parent="UICanvas/HUD/BossSkillArea/BossMeterWrap"]
unique_name_in_owner = true
layout_mode = 1
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
grow_horizontal = 2
grow_vertical = 2
mouse_filter = 2
texture = ExtResource("22")
expand_mode = 1
stretch_mode = 5

[node name="BossActionBarFill" type="TextureProgressBar" parent="UICanvas/HUD/BossSkillArea/BossMeterWrap"]
unique_name_in_owner = true
layout_mode = 1
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
grow_horizontal = 2
grow_vertical = 2
mouse_filter = 2
min_value = 0.0
max_value = 100.0
show_percentage = false
nine_patch_stretch = false
texture_progress = ExtResource("23")

[node name="BossWarnDividerMarker" type="TextureRect" parent="UICanvas/HUD/BossSkillArea/BossMeterWrap"]
unique_name_in_owner = true
layout_mode = 1
anchors_preset = 0
offset_left = -3.0
offset_top = 170.0
offset_right = 27.0
offset_bottom = 200.0
mouse_filter = 2
texture = ExtResource("24")
expand_mode = 1
stretch_mode = 5

[node name="IsWarningIcon" type="TextureRect" parent="UICanvas/HUD/BossSkillArea"]
unique_name_in_owner = true
visible = false
layout_mode = 1
anchors_preset = 0
offset_left = 3.975
offset_top = 177.975
offset_right = 33.975
offset_bottom = 207.975
mouse_filter = 2
texture = ExtResource("25")
expand_mode = 1
stretch_mode = 5

[node name="BossSkillIcon" type="TextureRect" parent="UICanvas/HUD/BossSkillArea"]
unique_name_in_owner = true
layout_mode = 1
anchors_preset = 0
offset_left = 37.5
offset_top = 12.5
offset_right = 194.5
offset_bottom = 122.5
mouse_filter = 2
texture = ExtResource("26")
expand_mode = 1
stretch_mode = 5

[node name="MistProgress" type="Control" parent="UICanvas/HUD"]
layout_mode = 1
anchors_preset = 0
anchor_left = 1.0
anchor_top = 0.24212963
anchor_right = 1.0
anchor_bottom = 0.24212963
offset_left = -228.0
offset_top = -107.5
offset_right = -13.0
offset_bottom = 107.5
grow_horizontal = 2
grow_vertical = 2
mouse_filter = 2

[node name="MistBackground" type="TextureRect" parent="UICanvas/HUD/MistProgress"]
layout_mode = 1
anchors_preset = 0
offset_left = 10.975
offset_top = 15.975
offset_right = 214.975
offset_bottom = 219.975
mouse_filter = 2
texture = ExtResource("27")
expand_mode = 1
stretch_mode = 5

[node name="MistRingTrack" type="TextureRect" parent="UICanvas/HUD/MistProgress"]
layout_mode = 1
anchors_preset = 0
offset_left = 23.975
offset_top = 28.975
offset_right = 199.975
offset_bottom = 204.975
mouse_filter = 2
texture = ExtResource("28")
expand_mode = 1
stretch_mode = 5

[node name="MistRingFill" type="TextureProgressBar" parent="UICanvas/HUD/MistProgress"]
unique_name_in_owner = true
layout_mode = 1
anchors_preset = 0
offset_left = 21.975
offset_top = 26.975
offset_right = 197.975
offset_bottom = 202.975
mouse_filter = 2
min_value = 0.0
max_value = 100.0
value = 100.0
nine_patch_stretch = false
texture_progress = ExtResource("29")

[node name="MistPctLabel" type="Label" parent="UICanvas/HUD/MistProgress"]
unique_name_in_owner = true
layout_mode = 1
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
offset_left = 67.975
offset_top = 93.975
offset_right = 147.975
offset_bottom = 125.975
theme_override_colors/font_outline_color = Color(0, 0, 0, 1)
theme_override_constants/outline_size = 4
theme_override_font_sizes/font_size = 18
text = "100%"
horizontal_alignment = 1
vertical_alignment = 1

[node name="TurnLabel" type="Label" parent="UICanvas/HUD"]
unique_name_in_owner = true
layout_mode = 1
anchors_preset = 8
anchor_left = 0.5
anchor_top = 1.0
anchor_right = 0.5
anchor_bottom = 1.0
offset_left = -400.0
offset_top = -270.0
offset_right = 400.0
offset_bottom = -246.0
grow_horizontal = 2
grow_vertical = 2
theme_override_colors/font_outline_color = Color(0.06, 0.02, 0.06, 1)
theme_override_constants/outline_size = 4
theme_override_font_sizes/font_size = 14
text = "回合信息"
horizontal_alignment = 1
""".strip()


def main() -> None:
    root = Path(__file__).resolve().parent
    gp = root / "gameplay.tscn"
    text = gp.read_text(encoding="utf-8")
    si = text.find(HUD_START_MARKER)
    if si == -1:
        raise SystemExit(f"HUD start marker not found: {HUD_START_MARKER}")
    ei = text.find(END_MARKER)
    if ei == -1:
        ei = len(text)
    elif ei <= si:
        raise SystemExit(f"invalid cut range si={si} ei={ei}")
    gp.write_text(text[:si] + HUD_BODY + "\n\n" + text[ei:], encoding="utf-8")
    print("HUD rebuilt")


if __name__ == "__main__":
    main()
