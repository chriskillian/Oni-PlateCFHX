using UnityEngine;

namespace PlateCounterflowHeatExchanger
{
    // Drives "stream B": a second liquid input and output that the vanilla BuildingDef
    // cannot provide (it gives only one primary input + one primary output). We resolve
    // the two port cells, register them with the liquid network by hand so pipes connect,
    // and move fluid through a small private buffer each sim tick.
    //
    // The buffer is deliberately separate from stream A's Storage: a counterflow exchanger
    // trades heat between streams, never mass, so the two streams must not share fluid.
    //
    // For this increment it is pure passthrough; no port icons and no heat math yet.
    public class SecondaryLiquidStream : KMonoBehaviour, ISim200ms
    {
        // Set by the building config before spawn.
        public CellOffset inputOffset;
        public CellOffset outputOffset;

        private const ConduitType Type = ConduitType.Liquid;
        private const float BufferCapacityKg = 10f; // one liquid packet's worth

        [MyCmpReq]
        private Building building;

        private int inputCell;
        private int outputCell;
        private FlowUtilityNetwork.NetworkItem inputNetworkItem;
        private FlowUtilityNetwork.NetworkItem outputNetworkItem;

        // The private buffer for stream B.
        private float bufferMass;
        private SimHashes bufferElement = SimHashes.Vacuum;
        private float bufferTemperature;
        private byte bufferDiseaseIdx = byte.MaxValue;
        private int bufferDiseaseCount;

        protected override void OnSpawn()
        {
            base.OnSpawn();

            int originCell = Grid.PosToCell(transform.GetPosition());
            inputCell = Grid.OffsetCell(originCell, building.GetRotatedOffset(inputOffset));
            outputCell = Grid.OffsetCell(originCell, building.GetRotatedOffset(outputOffset));

            IUtilityNetworkMgr mgr = Conduit.GetNetworkManager(Type);
            inputNetworkItem = new FlowUtilityNetwork.NetworkItem(Type, Endpoint.Sink, inputCell, gameObject);
            outputNetworkItem = new FlowUtilityNetwork.NetworkItem(Type, Endpoint.Source, outputCell, gameObject);
            mgr.AddToNetworks(inputCell, inputNetworkItem, true);
            mgr.AddToNetworks(outputCell, outputNetworkItem, true);
        }

        protected override void OnCleanUp()
        {
            IUtilityNetworkMgr mgr = Conduit.GetNetworkManager(Type);
            mgr.RemoveFromNetworks(inputCell, inputNetworkItem, true);
            mgr.RemoveFromNetworks(outputCell, outputNetworkItem, true);
            base.OnCleanUp();
        }

        public void Sim200ms(float dt)
        {
            ConduitFlow flow = Conduit.GetFlowManager(Type);

            // Pull one packet in only when the buffer is empty. This keeps the buffer to a
            // single element and avoids any temperature-blending logic for now.
            if (bufferMass <= 0f)
            {
                ConduitFlow.ConduitContents contents = flow.GetContents(inputCell);
                if (contents.mass > 0f)
                {
                    float toPull = Mathf.Min(BufferCapacityKg, contents.mass);
                    ConduitFlow.ConduitContents removed = flow.RemoveElement(inputCell, toPull);
                    bufferElement = removed.element;
                    bufferMass = removed.mass;
                    bufferTemperature = removed.temperature;
                    bufferDiseaseIdx = removed.diseaseIdx;
                    bufferDiseaseCount = removed.diseaseCount;
                }
            }

            // Push the buffer out. AddElement returns how much the output pipe accepted;
            // if it is back-pressured, the remainder stays in the buffer until next tick.
            if (bufferMass > 0f && bufferElement != SimHashes.Vacuum)
            {
                float accepted = flow.AddElement(
                    outputCell, bufferElement, bufferMass, bufferTemperature, bufferDiseaseIdx, bufferDiseaseCount);
                bufferMass -= accepted;
                if (bufferMass <= 0.0001f)
                {
                    bufferMass = 0f;
                    bufferElement = SimHashes.Vacuum;
                }
            }
        }
    }
}
