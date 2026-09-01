using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Client.MirControls;
using Client.MirGraphics;
using Client.MirObjects;
using Client.MirSounds;

namespace Client.MirScenes.Dialogs
{
    public sealed class CharacterDialog : MirImageControl
    {
        public MirButton CloseButton, CharacterButton, StatusButton, StateButton, SkillButton;
        public MirImageControl CharacterPage, StatusPage, StatePage, SkillPage, ClassImage;

        public MirLabel NameLabel, GuildLabel, LoverLabel;
        public MirLabel ACLabel, MACLabel, DCLabel, MCLabel, SCLabel, HealthLabel, ManaLabel;
        public MirLabel CritRLabel, CritDLabel, LuckLabel, AttkSpdLabel, AccLabel, AgilLabel;
        public MirLabel ExpPLabel, BagWLabel, WearWLabel, HandWLabel, MagicRLabel, PoisonRecLabel, HealthRLabel, ManaRLabel, PoisonResLabel, HolyTLabel, FreezeLabel, PoisonAtkLabel;
        public MirLabel HeadingLabel, StatLabel;
        public MirButton NextButton, BackButton;

        public MirItemCell[] Grid;
        private MirGridType GridType;
        public MagicButton[] Magics;

        public int StartIndex;
        private UserObject Actor;

        // per-dialog StateEffect mappings (keep local so Character has its own armour/weapon mappings)

        public struct StateEffectInfo
        {
            public int BaseIndex;
            public int Frames;
            public int MsPerFrame;
            public int OffsetX;
            public int OffsetY;
            public float Rate;

            public StateEffectInfo(int baseIndex, int frames, int msPerFrame, int offsetX = 0, int offsetY = -20, float rate = 1f)
            {
                BaseIndex = baseIndex;
                Frames = frames;
                MsPerFrame = msPerFrame;
                OffsetX = offsetX;
                OffsetY = offsetY;
                Rate = rate;
            }
        }

        // local per-dialog mappings (weapon/slot and armour by gender)
        private static readonly Dictionary<(int effectId, EquipmentSlot slot), StateEffectInfo> SlotMap = new Dictionary<(int, EquipmentSlot), StateEffectInfo>
        {
            { (68, EquipmentSlot.Weapon), new StateEffectInfo(968, 19, 200, 130, 270, 1f) },
            { (140, EquipmentSlot.Weapon), new StateEffectInfo(140, 10, 200, 0, -20, 1f) },
        };

        private static readonly Dictionary<(int effectId, MirGender gender), StateEffectInfo> ArmourMap = new Dictionary<(int, MirGender), StateEffectInfo>
        {
            { (1, MirGender.Male), new StateEffectInfo(880, 15, 200, 0, 260, 1f) },
            { (1, MirGender.Female), new StateEffectInfo(1144, 15, 200, 100, 260, 1f) },
        };

        private static bool TryGetSlotEffect(int effectId, EquipmentSlot slot, out StateEffectInfo info)
        {
            return SlotMap.TryGetValue((effectId, slot), out info);
        }

        private static bool TryGetArmourEffect(int effectId, MirGender gender, out StateEffectInfo info)
        {
            return ArmourMap.TryGetValue((effectId, gender), out info) || SlotMap.TryGetValue((effectId, EquipmentSlot.Armour), out info);
        }

        private static bool HasArmourMapping(int effectId, MirGender gender)
        {
            return ArmourMap.ContainsKey((effectId, gender)) || SlotMap.ContainsKey((effectId, EquipmentSlot.Armour));
        }

        private static bool HasSlotMapping(int effectId, EquipmentSlot slot)
        {
            return SlotMap.ContainsKey((effectId, slot));
        }

        // UI effect instances that advance independently
        private class UIEffect
        {
            public int BaseIndex;
            public int Count;
            public int Duration;

            public int CurrentFrame;
            public long Start;
            public long NextFrame;
            public bool Repeat = true;

            public UIEffect(int baseIndex, int count, int duration)
            {
                BaseIndex = baseIndex;
                Count = count == 0 ? 1 : count;
                Duration = duration;
                Start = CMain.Time;
                NextFrame = Start + (Duration / Count) * (CurrentFrame + 1);
            }

            public void Reset(int baseIndex, int count, int duration)
            {
                BaseIndex = baseIndex;
                Count = count == 0 ? 1 : count;
                Duration = duration;
                CurrentFrame = 0;
                Start = CMain.Time;
                NextFrame = Start + (Duration / Count) * (CurrentFrame + 1);
            }

            // Update CurrentFrame based on elapsed time so the animation advances correctly
            // even if Process/Draw calls are sporadic.
            public void UpdateFrame()
            {
                if (Count <= 1) { CurrentFrame = 0; return; }

                long elapsed = CMain.Time - Start;
                if (elapsed < 0) elapsed = 0;

                long frameDuration = Duration / Count;
                if (frameDuration <= 0) frameDuration = 1;

                long frame = (elapsed / frameDuration) % Count;
                CurrentFrame = (int)frame;
            }
        }

        // key is (effectId, slot) so the same effect id on different equipment slots can animate independently
        private readonly Dictionary<(int effectId, EquipmentSlot slot), UIEffect> ActiveUIEffects = new Dictionary<(int, EquipmentSlot), UIEffect>();
        private readonly System.Windows.Forms.Timer _updateTimer;

        private UIEffect GetOrCreateUIEffect(int effectKey, EquipmentSlot slot, int baseIndex, int frames, int msPerFrame)
        {
            var key = (effectKey, slot);
            if (!ActiveUIEffects.TryGetValue(key, out var e))
            {
                e = new UIEffect(baseIndex, frames, frames * msPerFrame);
                e.Repeat = true;
                ActiveUIEffects[key] = e;
                return e;
            }

            // If baseIndex/frames/duration changed, reset
            if (e.BaseIndex != baseIndex || e.Count != frames || e.Duration != frames * msPerFrame)
            {
                e.Reset(baseIndex, frames, frames * msPerFrame);
            }

            return e;
        }

        public CharacterDialog(MirGridType gridType, UserObject actor)
        {
            Actor = actor;
            GridType = gridType;

            Index = 504;
            Library = Libraries.Title;
            Location = new Point(Settings.ScreenWidth - 264, 0);
            Movable = true;
            Sort = true;            

            BeforeDraw += (o, e) => RefreshInterface();

            // timer to force periodic updates/redraws so UI effects animate even when there's no mouse activity
            _updateTimer = new System.Windows.Forms.Timer();
            _updateTimer.Interval = 50; // 20 FPS update for UI effects
            _updateTimer.Tick += (s, e) =>
            {
                // Advance frames for active UI effects based on current time
                foreach (var kv in ActiveUIEffects)
                {
                    kv.Value.UpdateFrame();
                }

                // Force the dialog to redraw so AfterDraw runs and effects are drawn
                try { Redraw(); } catch { }
            };
            _updateTimer.Start();

            CharacterPage = new MirImageControl
            {
                Index = 340,
                Parent = this,
                Library = Libraries.Prguse,
                Location = new Point(8, 90),
            };
            CharacterPage.AfterDraw += (o, e) =>
            {
                if (Libraries.StateItems == null) return;
                ItemInfo RealItem = null;
                if (Grid[(int)EquipmentSlot.Armour].Item != null)
                {
                    if (actor.WingEffect == 1 || actor.WingEffect == 2)
                    {
                        int wingOffset = actor.WingEffect == 1 ? 2 : 4;

                        int genderOffset = actor.Gender == MirGender.Male ? 0 : 1;

                        Libraries.Prguse2.DrawBlend(1200 + wingOffset + genderOffset, DisplayLocation, Color.White, true, 1F);
                    }

                    RealItem = Functions.GetRealItem(Grid[(int)EquipmentSlot.Armour].Item.Info, actor.Level, actor.Class, GameScene.ItemInfoList);
                    Libraries.StateItems.Draw(RealItem.Image, DisplayLocation, Color.White, true, 1F);

                    // If the equipped armour has a special effect draw an animated glow from StateEffect
                    // Only apply if a mapping exists in ArmourEffectMap or StateEffectMap
                    if (RealItem.Effect > 0 && Libraries.StateEffect != null)
                    {
                        int effectKeyCheck = RealItem.Effect;
                        bool hasArmourMapping = HasArmourMapping(effectKeyCheck, actor.Gender);
                        if (hasArmourMapping)
                        {
                            int effectKey = RealItem.Effect;
                            int baseIndex = effectKey;
                            int frames = 10;
                            int msPerFrame = 200;
                            var infoLocal = new StateEffectInfo(baseIndex, frames, msPerFrame, 0, -20, 1f);

                        // Prefer gender-specific armour mapping if present (local)
                        if (TryGetArmourEffect(effectKey, actor.Gender, out var genderMapped))
                        {
                            infoLocal = new StateEffectInfo(genderMapped.BaseIndex, genderMapped.Frames, genderMapped.MsPerFrame, genderMapped.OffsetX, genderMapped.OffsetY, genderMapped.Rate);
                            baseIndex = genderMapped.BaseIndex;
                            frames = genderMapped.Frames;
                            msPerFrame = genderMapped.MsPerFrame;
                        }
                        else if (TryGetSlotEffect(effectKey, EquipmentSlot.Armour, out var mapped))
                        {
                            infoLocal = new StateEffectInfo(mapped.BaseIndex, mapped.Frames, mapped.MsPerFrame, mapped.OffsetX, mapped.OffsetY, mapped.Rate);
                            baseIndex = mapped.BaseIndex;
                            frames = mapped.Frames;
                            msPerFrame = mapped.MsPerFrame;
                        }

                            var uiEffect = GetOrCreateUIEffect(effectKey, EquipmentSlot.Armour, baseIndex, frames, msPerFrame);
                            uiEffect.UpdateFrame();

                            int drawIndex = baseIndex + uiEffect.CurrentFrame;
                            Point effectLocation = new Point(DisplayLocation.X + infoLocal.OffsetX, DisplayLocation.Y + infoLocal.OffsetY);

                            if (drawIndex >= 0)
                                Libraries.StateEffect.DrawBlend(drawIndex, effectLocation, Color.White, true, infoLocal.Rate);
                        }
                    }
                }
                if (Grid[(int)EquipmentSlot.Weapon].Item != null)
                {
                    RealItem = Functions.GetRealItem(Grid[(int)EquipmentSlot.Weapon].Item.Info, actor.Level, actor.Class, GameScene.ItemInfoList);
                    Libraries.StateItems.Draw(RealItem.Image, DisplayLocation, Color.White, true, 1F);

                    // If the equipped weapon has a special effect (Effect > 0) draw an animated glow from StateEffect
                    // Only apply if a mapping exists in the centralized manager for weapon
                    if (RealItem.Effect > 0 && Libraries.StateEffect != null)
                    {
                        int effectKeyCheckW = RealItem.Effect;
                        if (HasSlotMapping(effectKeyCheckW, EquipmentSlot.Weapon))
                        {
                            int effectKey = RealItem.Effect;
                            int baseIndex = effectKey;
                            int frames = 10;
                            int msPerFrame = 200;

                            if (TryGetSlotEffect(effectKey, EquipmentSlot.Weapon, out var info))
                            {
                                baseIndex = info.BaseIndex;
                                frames = info.Frames;
                                msPerFrame = info.MsPerFrame;
                            }
                            // Use a UI effect instance so the glow advances frames itself (stateful)
                            var uiEffect = GetOrCreateUIEffect(effectKey, EquipmentSlot.Weapon, baseIndex, frames, msPerFrame);
                            uiEffect.UpdateFrame(); // compute current frame from start/time

                            int drawIndex = baseIndex + uiEffect.CurrentFrame;

                            // Determine offsets (defaults to 0, -20)
                            int offsetX = 0;
                            int offsetY = -20;
                            if (info.OffsetX != 0 || info.OffsetY != 0)
                            {
                                offsetX = info.OffsetX;
                                offsetY = info.OffsetY;
                            }

                            // Draw the state effect at the configured offset relative to the item display location
                            Point effectLocation = new Point(DisplayLocation.X + offsetX, DisplayLocation.Y + offsetY);

                            // Basic bounds check: skip if index negative
                            if (drawIndex >= 0)
                                Libraries.StateEffect.DrawBlend(drawIndex, effectLocation, Color.White, true, info.Rate);
                        }
                    }

                }

                if (Grid[(int)EquipmentSlot.Helmet].Item != null)
                    Libraries.StateItems.Draw(Grid[(int)EquipmentSlot.Helmet].Item.Info.Image, DisplayLocation, Color.White, true, 1F);
                else
                {
                    int hair = 441 + actor.Hair + (actor.Class == MirClass.Assassin ? 20 : 0) + (actor.Gender == MirGender.Male ? 0 : 40);

                    int offSetX = actor.Class == MirClass.Assassin ? (actor.Gender == MirGender.Male ? 6 : 4) : 0;
                    int offSetY = actor.Class == MirClass.Assassin ? (actor.Gender == MirGender.Male ? 25 : 18) : 0;

                    Libraries.Prguse.Draw(hair, new Point(DisplayLocation.X + offSetX, DisplayLocation.Y + offSetY), Color.White, true, 1F);
                }
            };

            StatusPage = new MirImageControl
            {
                Index = 506,
                Parent = this,
                Library = Libraries.Title,
                Location = new Point(8, 90),
                Visible = false,
            };
            StatusPage.BeforeDraw += (o, e) =>
            {
                ACLabel.Text = string.Format("{0}-{1}", actor.Stats[Stat.MinAC], actor.Stats[Stat.MaxAC]);
                MACLabel.Text = string.Format("{0}-{1}", actor.Stats[Stat.MinMAC], actor.Stats[Stat.MaxMAC]);
                DCLabel.Text = string.Format("{0}-{1}", actor.Stats[Stat.MinDC], actor.Stats[Stat.MaxDC]);
                MCLabel.Text = string.Format("{0}-{1}", actor.Stats[Stat.MinMC], actor.Stats[Stat.MaxMC]);
                SCLabel.Text = string.Format("{0}-{1}", actor.Stats[Stat.MinSC], actor.Stats[Stat.MaxSC]);
                HealthLabel.Text = string.Format("{0}/{1}", actor.HP, actor.Stats[Stat.HP]);
                ManaLabel.Text = string.Format("{0}/{1}", actor.MP, actor.Stats[Stat.MP]);
                CritRLabel.Text = string.Format("{0}%", actor.Stats[Stat.CriticalRate]);
                CritDLabel.Text = string.Format("{0}", actor.Stats[Stat.CriticalDamage]);
                AttkSpdLabel.Text = string.Format("{0}", actor.Stats[Stat.AttackSpeed]);
                AccLabel.Text = string.Format("+{0}", actor.Stats[Stat.Accuracy]);
                AgilLabel.Text = string.Format("+{0}", actor.Stats[Stat.Agility]);
                LuckLabel.Text = string.Format("{0}", actor.Stats[Stat.Luck]);
            };

            StatePage = new MirImageControl
            {
                Index = 507,
                Parent = this,
                Library = Libraries.Title,
                Location = new Point(8, 90),
                Visible = false
            };
            StatePage.BeforeDraw += (o, e) =>
            {
                ExpPLabel.Text = string.Format("{0:0.##%}", actor.Experience / (double)actor.MaxExperience);
                BagWLabel.Text = string.Format("{0}/{1}", actor.CurrentBagWeight, actor.Stats[Stat.BagWeight]);
                WearWLabel.Text = string.Format("{0}/{1}", actor.CurrentWearWeight, actor.Stats[Stat.WearWeight]);
                HandWLabel.Text = string.Format("{0}/{1}", actor.CurrentHandWeight, actor.Stats[Stat.HandWeight]);
                MagicRLabel.Text = string.Format("+{0}", actor.Stats[Stat.MagicResist]);
                PoisonResLabel.Text = string.Format("+{0}", actor.Stats[Stat.PoisonResist]);
                HealthRLabel.Text = string.Format("+{0}", actor.Stats[Stat.HealthRecovery]);
                ManaRLabel.Text = string.Format("+{0}", actor.Stats[Stat.SpellRecovery]);
                PoisonRecLabel.Text = string.Format("+{0}", actor.Stats[Stat.PoisonRecovery]);
                HolyTLabel.Text = string.Format("+{0}", actor.Stats[Stat.Holy]);
                FreezeLabel.Text = string.Format("+{0}", actor.Stats[Stat.Freezing]);
                PoisonAtkLabel.Text = string.Format("+{0}", actor.Stats[Stat.PoisonAttack]);
            };


            SkillPage = new MirImageControl
            {
                Index = 508,
                Parent = this,
                Library = Libraries.Title,
                Location = new Point(8, 90),
                Visible = false
            };


            CharacterButton = new MirButton
            {
                Index = 500,
                Library = Libraries.Title,
                Location = new Point(8, 70),
                Parent = this,
                PressedIndex = 500,
                Size = new Size(64, 20),
                Sound = SoundList.ButtonA,
            };
            CharacterButton.Click += (o, e) => ShowCharacterPage();
            StatusButton = new MirButton
            {
                Library = Libraries.Title,
                Location = new Point(70, 70),
                Parent = this,
                PressedIndex = 501,
                Size = new Size(64, 20),
                Sound = SoundList.ButtonA
            };
            StatusButton.Click += (o, e) => ShowStatusPage();

            StateButton = new MirButton
            {
                Library = Libraries.Title,
                Location = new Point(132, 70),
                Parent = this,
                PressedIndex = 502,
                Size = new Size(64, 20),
                Sound = SoundList.ButtonA
            };
            StateButton.Click += (o, e) => ShowStatePage();

            SkillButton = new MirButton
            {
                Library = Libraries.Title,
                Location = new Point(194, 70),
                Parent = this,
                PressedIndex = 503,
                Size = new Size(64, 20),
                Sound = SoundList.ButtonA
            };
            SkillButton.Click += (o, e) => ShowSkillPage();

            CloseButton = new MirButton
            {
                HoverIndex = 361,
                Index = 360,
                Location = new Point(241, 3),
                Library = Libraries.Prguse2,
                Parent = this,
                PressedIndex = 362,
                Sound = SoundList.ButtonA,
            };
            CloseButton.Click += (o, e) => Hide();

            NameLabel = new MirLabel
            {
                DrawFormat = TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter,
                Parent = this,
                Location = new Point(0, 12),
                Size = new Size(264, 20),
                NotControl = true,
            };
            GuildLabel = new MirLabel
            {
                DrawFormat = TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter,
                Parent = this,
                Location = new Point(0, 33),
                Size = new Size(264, 30),
                NotControl = true,
            };
            ClassImage = new MirImageControl
            {
                Index = 100,
                Library = Libraries.Prguse,
                Location = new Point(15, 33),
                Parent = this,
                NotControl = true,
            };

            Grid = new MirItemCell[Enum.GetNames(typeof(EquipmentSlot)).Length];

            Grid[(int)EquipmentSlot.Weapon] = new MirItemCell
            {
                ItemSlot = (int)EquipmentSlot.Weapon,
                GridType = gridType,
                Parent = CharacterPage,
                Location = new Point(123, 7),
            };


            Grid[(int)EquipmentSlot.Armour] = new MirItemCell
            {
                ItemSlot = (int)EquipmentSlot.Armour,
                GridType = gridType,
                Parent = CharacterPage,
                Location = new Point(163, 7),
            };


            Grid[(int)EquipmentSlot.Helmet] = new MirItemCell
            {
                ItemSlot = (int)EquipmentSlot.Helmet,
                GridType = gridType,
                Parent = CharacterPage,
                Location = new Point(203, 7),
            };



            Grid[(int)EquipmentSlot.Torch] = new MirItemCell
            {
                ItemSlot = (int)EquipmentSlot.Torch,
                GridType = gridType,
                Parent = CharacterPage,
                Location = new Point(203, 134),
            };


            Grid[(int)EquipmentSlot.Necklace] = new MirItemCell
            {
                ItemSlot = (int)EquipmentSlot.Necklace,
                GridType = gridType,
                Parent = CharacterPage,
                Location = new Point(203, 98),
            };


            Grid[(int)EquipmentSlot.BraceletL] = new MirItemCell
            {
                ItemSlot = (int)EquipmentSlot.BraceletL,
                GridType = gridType,
                Parent = CharacterPage,
                Location = new Point(8, 170),
            };

            Grid[(int)EquipmentSlot.BraceletR] = new MirItemCell
            {
                ItemSlot = (int)EquipmentSlot.BraceletR,
                GridType = gridType,
                Parent = CharacterPage,
                Location = new Point(203, 170),
            };

            Grid[(int)EquipmentSlot.RingL] = new MirItemCell
            {
                ItemSlot = (int)EquipmentSlot.RingL,
                GridType = gridType,
                Parent = CharacterPage,
                Location = new Point(8, 206),
            };

            Grid[(int)EquipmentSlot.RingR] = new MirItemCell
            {
                ItemSlot = (int)EquipmentSlot.RingR,
                GridType = gridType,
                Parent = CharacterPage,
                Location = new Point(203, 206),
            };


            Grid[(int)EquipmentSlot.Amulet] = new MirItemCell
            {
                ItemSlot = (int)EquipmentSlot.Amulet,
                GridType = gridType,
                Parent = CharacterPage,
                Location = new Point(8, 242),
            };


            Grid[(int)EquipmentSlot.Boots] = new MirItemCell
            {
                ItemSlot = (int)EquipmentSlot.Boots,
                GridType = gridType,
                Parent = CharacterPage,
                Location = new Point(48, 242),
            };

            Grid[(int)EquipmentSlot.Belt] = new MirItemCell
            {
                ItemSlot = (int)EquipmentSlot.Belt,
                GridType = gridType,
                Parent = CharacterPage,
                Location = new Point(88, 242),
            };


            Grid[(int)EquipmentSlot.Stone] = new MirItemCell
            {
                ItemSlot = (int)EquipmentSlot.Stone,
                GridType = gridType,
                Parent = CharacterPage,
                Location = new Point(128, 242),
            };

            Grid[(int)EquipmentSlot.Mount] = new MirItemCell
            {
                ItemSlot = (int)EquipmentSlot.Mount,
                GridType = gridType,
                Parent = CharacterPage,
                Location = new Point(203, 62),
            };

            // STATS I
            HealthLabel = new MirLabel
            {
                AutoSize = true,
                Parent = StatusPage,
                Location = new Point(126, 20),
                NotControl = true,
                Text = "0-0",
            };

            ManaLabel = new MirLabel
            {
                AutoSize = true,
                Parent = StatusPage,
                Location = new Point(126, 38),
                NotControl = true,
                Text = "0-0",
            };

            ACLabel = new MirLabel
            {
                AutoSize = true,
                Parent = StatusPage,
                Location = new Point(126, 56),
                NotControl = true,
                Text = "0-0",
            };

            MACLabel = new MirLabel
            {
                AutoSize = true,
                Parent = StatusPage,
                Location = new Point(126, 74),
                NotControl = true,
                Text = "0-0",
            };
            DCLabel = new MirLabel
            {
                AutoSize = true,
                Parent = StatusPage,
                Location = new Point(126, 92),
                NotControl = true,
                Text = "0-0"
            };
            MCLabel = new MirLabel
            {
                AutoSize = true,
                Parent = StatusPage,
                Location = new Point(126, 110),
                NotControl = true,
                Text = "0/0"
            };
            SCLabel = new MirLabel
            {
                AutoSize = true,
                Parent = StatusPage,
                Location = new Point(126, 128),
                NotControl = true,
                Text = "0/0"
            };
            //Breezer - New Labels
            CritRLabel = new MirLabel
            {
                AutoSize = true,
                Parent = StatusPage,
                Location = new Point(126, 146),
                NotControl = true
            };
            CritDLabel = new MirLabel
            {
                AutoSize = true,
                Parent = StatusPage,
                Location = new Point(126, 164),
                NotControl = true
            };
            AttkSpdLabel = new MirLabel
            {
                AutoSize = true,
                Parent = StatusPage,
                Location = new Point(126, 182),
                NotControl = true
            };
            AccLabel = new MirLabel
            {
                AutoSize = true,
                Parent = StatusPage,
                Location = new Point(126, 200),
                NotControl = true
            };
            AgilLabel = new MirLabel
            {
                AutoSize = true,
                Parent = StatusPage,
                Location = new Point(126, 218),
                NotControl = true
            };
            LuckLabel = new MirLabel
            {
                AutoSize = true,
                Parent = StatusPage,
                Location = new Point(126, 236),
                NotControl = true
            };
            // STATS II 
            ExpPLabel = new MirLabel
            {
                AutoSize = true,
                Parent = StatePage,
                Location = new Point(126, 20),
                NotControl = true,
                Text = "0-0",
            };

            BagWLabel = new MirLabel
            {
                AutoSize = true,
                Parent = StatePage,
                Location = new Point(126, 38),
                NotControl = true,
                Text = "0-0",
            };

            WearWLabel = new MirLabel
            {
                AutoSize = true,
                Parent = StatePage,
                Location = new Point(126, 56),
                NotControl = true,
                Text = "0-0",
            };

            HandWLabel = new MirLabel
            {
                AutoSize = true,
                Parent = StatePage,
                Location = new Point(126, 74),
                NotControl = true,
                Text = "0-0",
            };
            MagicRLabel = new MirLabel
            {
                AutoSize = true,
                Parent = StatePage,
                Location = new Point(126, 92),
                NotControl = true,
                Text = "0-0"
            };
            PoisonResLabel = new MirLabel
            {
                AutoSize = true,
                Parent = StatePage,
                Location = new Point(126, 110),
                NotControl = true,
                Text = "0/0"
            };
            HealthRLabel = new MirLabel
            {
                AutoSize = true,
                Parent = StatePage,
                Location = new Point(126, 128),
                NotControl = true,
                Text = "0/0"
            };
            //Breezer
            ManaRLabel = new MirLabel
            {
                AutoSize = true,
                Parent = StatePage,
                Location = new Point(126, 146),
                NotControl = true
            };
            PoisonRecLabel = new MirLabel
            {
                AutoSize = true,
                Parent = StatePage,
                Location = new Point(126, 164),
                NotControl = true
            };
            HolyTLabel = new MirLabel
            {
                AutoSize = true,
                Parent = StatePage,
                Location = new Point(126, 182),
                NotControl = true
            };
            FreezeLabel = new MirLabel
            {
                AutoSize = true,
                Parent = StatePage,
                Location = new Point(126, 200),
                NotControl = true
            };
            PoisonAtkLabel = new MirLabel
            {
                AutoSize = true,
                Parent = StatePage,
                Location = new Point(126, 218),
                NotControl = true
            };

            Magics = new MagicButton[7];

            for (int i = 0; i < Magics.Length; i++)
                Magics[i] = new MagicButton 
                { 
                    Parent = SkillPage, 
                    Visible = false, 
                    Location = new Point(8, 8 + i * 33),
                    HeroMagic = gridType == MirGridType.HeroEquipment
                };

            NextButton = new MirButton
            {
                Index = 396,
                Location = new Point(140, 250),
                Library = Libraries.Prguse,
                Parent = SkillPage,
                PressedIndex = 397,
                Sound = SoundList.ButtonA,
            };
            NextButton.Click += (o, e) =>
            {
                if (StartIndex + 7 >= actor.Magics.Count) return;

                StartIndex += 7;
                RefreshInterface();
            };

            BackButton = new MirButton
            {
                Index = 398,
                Location = new Point(90, 250),
                Library = Libraries.Prguse,
                Parent = SkillPage,
                PressedIndex = 399,
                Sound = SoundList.ButtonA,
            };
            BackButton.Click += (o, e) =>
            {
                if (StartIndex - 7 < 0) return;

                StartIndex -= 7;
                RefreshInterface();
            };
        }

        public override void Show()
        {
            if (Visible) return;
            Visible = true;
        }

        public override void Hide()
        {
            GameScene.Scene.SocketDialog.Hide();
            base.Hide();
        }

        public void ShowCharacterPage()
        {
            CharacterPage.Visible = true;
            StatusPage.Visible = false;
            StatePage.Visible = false;
            SkillPage.Visible = false;
            CharacterButton.Index = 500;
            StatusButton.Index = -1;
            StateButton.Index = -1;
            SkillButton.Index = -1;
        }

        private void ShowStatusPage()
        {
            CharacterPage.Visible = false;
            StatusPage.Visible = true;
            StatePage.Visible = false;
            SkillPage.Visible = false;
            CharacterButton.Index = -1;
            StatusButton.Index = 501;
            StateButton.Index = -1;
            SkillButton.Index = -1;
        }

        private void ShowStatePage()
        {
            CharacterPage.Visible = false;
            StatusPage.Visible = false;
            StatePage.Visible = true;
            SkillPage.Visible = false;
            CharacterButton.Index = -1;
            StatusButton.Index = -1;
            StateButton.Index = 502;
            SkillButton.Index = -1;
        }

        public void ShowSkillPage()
        {
            CharacterPage.Visible = false;
            StatusPage.Visible = false;
            StatePage.Visible = false;
            SkillPage.Visible = true;
            CharacterButton.Index = -1;
            StatusButton.Index = -1;
            StateButton.Index = -1;
            SkillButton.Index = 503;
            //StartIndex = 0;
        }

        private void RefreshInterface()
        {
            int offSet = Actor.Gender == MirGender.Male ? 0 : 1;

            Index = 504;// +offSet;
            CharacterPage.Index = 340 + offSet;

            switch (Actor.Class)
            {
                case MirClass.Warrior:
                    ClassImage.Index = 100;// + offSet * 5;
                    break;
                case MirClass.Wizard:
                    ClassImage.Index = 101;// + offSet * 5;
                    break;
                case MirClass.Taoist:
                    ClassImage.Index = 102;// + offSet * 5;
                    break;
                case MirClass.Assassin:
                    ClassImage.Index = 103;// + offSet * 5;
                    break;
                case MirClass.Archer:
                    ClassImage.Index = 104;// + offSet * 5;
                    break;
            }

            NameLabel.Text = Actor.Name;
            GuildLabel.Text = Actor.GuildName + " " + Actor.GuildRankName;

            for (int i = 0; i < Magics.Length; i++)
            {
                if (i + StartIndex >= Actor.Magics.Count)
                {
                    Magics[i].Visible = false;
                    continue;
                }

                Magics[i].Visible = true;
                Magics[i].Update(Actor.Magics[i + StartIndex]);
            }
        }

        public MirItemCell GetCell(ulong id)
        {

            for (int i = 0; i < Grid.Length; i++)
            {
                if (Grid[i].Item == null || Grid[i].Item.UniqueID != id) continue;
                return Grid[i];
            }
            return null;
        }

    }
}
