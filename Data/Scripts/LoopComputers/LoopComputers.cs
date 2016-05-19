using System;
using System.Linq;
using Sandbox.Common;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using Digi.Utils;
using VRageMath;

namespace Digi.LoopComputers
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class LoopComputersMod : MySessionComponentBase
    {
        public static bool init = false;
        
        private void Init()
        {
            init = true;
            
            Log.Init();
            Log.Info("Initialized.");
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
        
        public override void UpdateAfterSimulation()
        {
            try
            {
                if(!init)
                {
                    if(MyAPIGateway.Multiplayer == null || !MyAPIGateway.Multiplayer.IsServer)
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
        private int delayTicks = 0;
        private int tick = 0;
        
        private const string TAG_START = "[repeat";
        private const string TAG_END = "]";
        private const int MIN_TICKS = 10;
        private const int MAX_TICKS = 60*60*10;
        
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
            NameChanged(block);
        }
        
        public override void Close()
        {
            var block = Entity as IMyTerminalBlock;
            block.CustomNameChanged -= NameChanged;
        }
        
        public void NameChanged(IMyTerminalBlock b)
        {
            try
            {
                var block = b as IMyFunctionalBlock;
                var name = block.CustomName.ToLower();
                delayTicks = 0;
                
                if(name.Contains(TAG_START + TAG_END))
                {
                    delayTicks = 60;
                }
                else if(name.Contains(TAG_START))
                {
                    int startIndex = name.IndexOf(TAG_START) + TAG_START.Length;
                    int endIndex = name.IndexOf(TAG_END, startIndex);
                    
                    if(startIndex == -1 || endIndex == -1)
                    {
                        delayTicks = 0;
                        return;
                    }
                    
                    string arg = name.Substring(startIndex, endIndex - startIndex).Trim(' ', ':', '=');
                    double delay;
                    
                    if(double.TryParse(arg, out delay))
                        delayTicks = (int)MathHelper.Clamp(delay * 60, MIN_TICKS, MAX_TICKS);
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
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
                
                if(delayTicks == 0)
                    return;
                
                var block = Entity as IMyFunctionalBlock;
                
                if(block.Enabled && block.IsWorking && block.IsFunctional && ++tick >= delayTicks)
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
    }
}