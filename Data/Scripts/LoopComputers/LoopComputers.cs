using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game.Components;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using Digi.Utils;
using VRageMath;
using Ingame = Sandbox.ModAPI.Ingame;

namespace Digi.LoopComputers
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class LoopComputersMod : MySessionComponentBase
    {
        public static bool init = false;
        
        private void Init()
        {
            init = true;
            
            Log.Init();
            Log.Info("Initialized.");
            
            MyAPIGateway.TerminalControls.CustomControlGetter += CustomControlGetter;
        }
        
        protected override void UnloadData()
        {
            try
            {
                if(init)
                {
                    init = false;
                    Log.Info("Mod unloaded.");
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
            
            Log.Close();
        }
        
        public static void CustomControlGetter(IMyTerminalBlock block, List<IMyTerminalControl> controls)
        {
            if(!(block is Ingame.IMyProgrammableBlock))
                return;
            
            var tc = MyAPIGateway.TerminalControls;
            
            controls.AddOrInsert(tc.CreateControl<IMyTerminalControlSeparator, Ingame.IMyProgrammableBlock>(string.Empty), 9);
            
            var c = tc.CreateControl<IMyTerminalControlSlider, Ingame.IMyProgrammableBlock>("Digi.LoopComputers.RepeatTime");
            c.Title = MyStringId.GetOrCompute("Self run");
            c.Tooltip = MyStringId.GetOrCompute("The block runs itself at the specified interval.\nValues smaller than 0.016s (one tick) are considered off.");
            c.SupportsMultipleBlocks = true;
            c.SetLogLimits(0.015f, 600f);
            c.Setter = (b, v) => b.GameLogic.GetAs<LoopPB>().DelayTime = (v < 0.016f ? 0 : v);
            c.Getter = (b) => (b.GameLogic.GetAs<LoopPB>().DelayTime);
            c.Writer = delegate(IMyTerminalBlock b, StringBuilder s)
            {
                float v = b.GameLogic.GetAs<LoopPB>().DelayTime;
                
                if(v < 0.016f)
                    s.Append("Off");
                else
                    s.AppendFormat("{0:0.000}s / {1}ticks", v, Math.Round(v * 60));
            };
            
            controls.AddOrInsert(c, 10);
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
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
    }
    
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_MyProgrammableBlock))]
    public class LoopPB : MyGameLogicComponent
    {
        private bool first = true;
        private float delayTime = 0;
        private int tick = 0;
        private byte propertiesChangedDelay = 0;
        
        private static readonly StringBuilder str = new StringBuilder();
        
        private const string DATA_TAG_START = "{LoopComputers:";
        private const char DATA_TAG_END = '}';
        
        private const string LEGACY_TAG_START = "[repeat";
        private const string LEGACY_TAG_END = "]";
        
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            Entity.NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;
        }
        
        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
        {
            return Entity.GetObjectBuilder(copy);
        }
        
        private void FirstUpdate()
        {
            var block = Entity as IMyTerminalBlock;
            block.CustomNameChanged += NameChanged;
            ReadLegacyName(block);
            NameChanged(block);
        }
        
        public override void Close()
        {
            var block = Entity as IMyTerminalBlock;
            block.CustomNameChanged -= NameChanged;
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
            var block = Entity as IMyTerminalBlock;
            str.Clear();
            str.Append(forceName ?? GetNameNoData().Trim());
            
            if(delayTime > 0)
            {
                str.Append(' ', 3);
                str.Append(DATA_TAG_START);
                str.Append(delayTime);
                str.Append(DATA_TAG_END);
            }
            
            block.SetCustomName(str.ToString());
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
                if(first)
                {
                    if(!LoopComputersMod.init)
                        return;
                    
                    first = false;
                    FirstUpdate();
                    return;
                }
                
                if(!MyAPIGateway.Multiplayer.IsServer)
                    return;
                
                if(propertiesChangedDelay > 0 && --propertiesChangedDelay == 0)
                    SaveToName();
                
                if(delayTime < 0.016f)
                    return;
                
                var block = Entity as IMyFunctionalBlock;
                
                if(block.Enabled && block.IsWorking && block.IsFunctional && ++tick >= (delayTime * 60))
                {
                    tick = 0;
                    block.GetActionWithName("Run").Apply(block);
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