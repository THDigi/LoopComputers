using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace Digi.LoopComputers
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class LoopComputersMod : MySessionComponentBase
    {
        public override void LoadData()
        {
            instance = this;
            Log.SetUp("Loop Computers", 400037065, "LoopComputers");
        }

        public static LoopComputersMod instance = null;

        public bool init = false;
        public bool initTerminalUI = false;
        public readonly StringBuilder str = new StringBuilder();
        public readonly Dictionary<long, LoopPB> PBs = new Dictionary<long, LoopPB>();

        private IMyTerminalControl separator = null;

        public const string SLIDER_ID = "LoopComputers.RepeatTime";
        public const string AFTER_CONTROL_ID = "Recompile";

        private void Init()
        {
            init = true;

            Log.Init();

            MyAPIGateway.TerminalControls.CustomControlGetter += CustomControlGetter;

            // HACK disabled to allow PBs to update using this session component
            //MyAPIGateway.Utilities.InvokeOnGameThread(() => SetUpdateOrder(MyUpdateOrder.NoUpdate));
        }

        protected override void UnloadData()
        {
            try
            {
                if(init)
                {
                    init = false;

                    MyAPIGateway.TerminalControls.CustomControlGetter -= CustomControlGetter;
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }

            instance = null;
            Log.Close();
        }

        // move this mods' terminal controls right after the control specified in AFTER_CONTROL_ID
        public void CustomControlGetter(IMyTerminalBlock block, List<IMyTerminalControl> controls)
        {
            if(block is IMyProgrammableBlock)
            {
                var index = controls.FindIndex((m) => m.Id == SLIDER_ID);

                if(index == -1)
                    return;

                if(separator == null)
                    separator = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyProgrammableBlock>(string.Empty);

                var runIndex = controls.FindIndex((m) => m.Id == AFTER_CONTROL_ID);
                var c = controls[index];
                controls.RemoveAt(index);
                controls.AddOrInsert(separator, runIndex + 1);
                controls.AddOrInsert(c, runIndex + 2);
            }
        }

        public override void UpdateBeforeSimulation()
        {
            try
            {
                if(!init)
                {
                    if(MyAPIGateway.Session == null || MyAPIGateway.Multiplayer == null)
                        return;

                    Init();
                }

                // HACK update PBs here...
                foreach(var pb in PBs)
                {
                    pb.Value.UpdateBeforeSimulation();
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_MyProgrammableBlock), useEntityUpdate: false)]
    public class LoopPB : MyGameLogicComponent
    {
        private bool first = true;
        private float delayTime = 0;
        private int tick = 0;
        private byte propertiesChangedDelay = 0;

        private const string DATA_TAG_START = "{LoopComputers:";
        private const char DATA_TAG_END = '}';

        private const string LEGACY_TAG_START = "[repeat";
        private const string LEGACY_TAG_END = "]";

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            // using InvokeOnGameThread() since Init() can be async
            MyAPIGateway.Utilities.InvokeOnGameThread(() => LoopComputersMod.instance.PBs.Add(Entity.EntityId, this));

            // HACK disabled since gamelogic update doesn't work on PBs right now, workaround added above.
            //NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME;
        }

        private void FirstUpdate()
        {
            var block = (IMyTerminalBlock)Entity;
            block.CustomNameChanged += NameChanged;
            ReadLegacyName(block);
            NameChanged(block);

            if(!LoopComputersMod.instance.initTerminalUI)
            {
                LoopComputersMod.instance.initTerminalUI = true;

                var c = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyProgrammableBlock>(LoopComputersMod.SLIDER_ID);
                c.Title = MyStringId.GetOrCompute("Auto-run");
                c.Tooltip = MyStringId.GetOrCompute("The block runs itself at the specified interval.\nValues smaller than 0.016s (one tick) are considered off.");
                c.SupportsMultipleBlocks = true;
                c.SetLogLimits(0.015f, 600f); // this min value can't be 0 or it screws up the math...
                c.Enabled = (b) => (!MyAPIGateway.Session.SessionSettings.EnableScripterRole || MyAPIGateway.Session.PromoteLevel >= MyPromoteLevel.Scripter);

                // HACK had to rewrite these since gamelogic doesn't return the class either.
                c.Setter = (b, v) => LoopComputersMod.instance.PBs[b.EntityId].DelayTime = (v < 0.016f ? 0 : v);
                c.Getter = (b) => LoopComputersMod.instance.PBs[b.EntityId].DelayTime;
                c.Writer = delegate (IMyTerminalBlock b, StringBuilder s)
                {
                    float v = LoopComputersMod.instance.PBs[b.EntityId].DelayTime;

                    if(v < 0.016f)
                    {
                        s.Append("Off");
                    }
                    else
                    {
                        var ticks = (int)Math.Round(v * 60);
                        s.AppendFormat("{0:0.000}s / {1}tick{2}", v, ticks, (ticks == 1 ? "" : "s"));
                    }
                };
#if false
                c.Setter = (b, v) => b.GameLogic.GetAs<LoopPB>().DelayTime = (v < 0.016f ? 0 : v);
                c.Getter = (b) => (b.GameLogic.GetAs<LoopPB>().DelayTime);
                c.Writer = delegate (IMyTerminalBlock b, StringBuilder s)
                {
                    float v = b.GameLogic.GetAs<LoopPB>().DelayTime;

                    if(v < 0.016f)
                    {
                        s.Append("Off");
                    }
                    else
                    {
                        var ticks = (int)Math.Round(v * 60);
                        s.AppendFormat("{0:0.000}s / {1}tick{2}", v, ticks, (ticks == 1 ? "" : "s"));
                    }
                };
#endif

                MyAPIGateway.TerminalControls.AddControl<IMyProgrammableBlock>(c);
            }
        }

        public override void Close()
        {
            try
            {
                LoopComputersMod.instance?.PBs.Remove(Entity.EntityId);

                var block = (IMyTerminalBlock)Entity;
                block.CustomNameChanged -= NameChanged;
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public float DelayTime
        {
            get { return delayTime; }
            set
            {
                delayTime = (float)Math.Round(value, 3);

                if(propertiesChangedDelay <= 0)
                    propertiesChangedDelay = 30;
            }
        }

        public void NameChanged(IMyTerminalBlock block)
        {
            try
            {
                delayTime = 0;

                var name = block.CustomName.ToLower();
                var startIndex = name.IndexOf(DATA_TAG_START, StringComparison.OrdinalIgnoreCase);

                if(startIndex == -1)
                    return;

                startIndex += DATA_TAG_START.Length;
                var endIndex = name.IndexOf(DATA_TAG_END, startIndex);

                if(endIndex == -1)
                    return;

                var data = name.Substring(startIndex, (endIndex - startIndex));
                delayTime = (float)Math.Round(float.Parse(data), 3);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        private void SaveToName(string forceName = null)
        {
            var block = (IMyTerminalBlock)Entity;
            var str = LoopComputersMod.instance.str;
            str.Clear();
            str.Append(forceName ?? GetNameNoData().Trim());

            if(delayTime > 0)
            {
                str.Append(' ', 3);
                str.Append(DATA_TAG_START);
                str.Append(delayTime);
                str.Append(DATA_TAG_END);
            }

            block.CustomName = str.ToString();
            str.Clear();
        }

        private string GetNameNoData()
        {
            var block = Entity as IMyTerminalBlock;
            var name = block.CustomName;
            var startIndex = name.IndexOf(DATA_TAG_START, StringComparison.OrdinalIgnoreCase);

            if(startIndex == -1)
                return name;

            var nameNoData = name.Substring(0, startIndex);
            var endIndex = name.IndexOf(DATA_TAG_END, startIndex);

            if(endIndex == -1)
                return nameNoData;
            else
                return nameNoData + name.Substring(endIndex + 1);
        }

        public override void UpdateBeforeSimulation()
        {
            try
            {
                var pb = (IMyProgrammableBlock)Entity;

                if(pb.CubeGrid.Physics == null)
                    return;

                if(first)
                {
                    if(LoopComputersMod.instance == null || !LoopComputersMod.instance.init)
                        return;

                    first = false;
                    FirstUpdate();
                    return;
                }

                if(propertiesChangedDelay > 0 && --propertiesChangedDelay == 0)
                    SaveToName();

                if(!MyAPIGateway.Multiplayer.IsServer)
                    return;

                if(delayTime < 0.016f)
                    return;

                if(pb.Enabled && pb.IsFunctional && pb.IsWorking && ++tick >= (delayTime * 60))
                {
                    tick = 0;
                    pb.Run();
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        private void ReadLegacyName(IMyTerminalBlock block)
        {
            var name = block.CustomName.ToLower();
            delayTime = 0;

            if(name.Contains(LEGACY_TAG_START + LEGACY_TAG_END))
            {
                delayTime = 1;
            }
            else if(name.Contains(LEGACY_TAG_START))
            {
                int startIndex = name.IndexOf(LEGACY_TAG_START, StringComparison.Ordinal) + LEGACY_TAG_START.Length;
                int endIndex = name.IndexOf(LEGACY_TAG_END, startIndex, StringComparison.Ordinal);

                if(startIndex == -1 || endIndex == -1)
                {
                    delayTime = 0;
                    return;
                }

                string arg = name.Substring(startIndex, endIndex - startIndex).Trim(' ', ':', '=');
                float delay;

                if(float.TryParse(arg, out delay))
                    delayTime = MathHelper.Clamp(delay, 0.016f, 600);
            }

            SaveToName(Regex.Replace(block.CustomName, @"\[repeat([\s\:\=][\d.]+|)\]", "", RegexOptions.IgnoreCase).Trim());
        }
    }
}