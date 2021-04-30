using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Bolt;
using Ludiq;

public class BoltFlowMachineTest : MonoBehaviour
{
    public FlowMacro flowMacro;

    private GraphReference graphReference;

    [ContextMenu("Edit Graph")]
    void EditGraph()
    {
        if (graphReference.IsUnityNull())
        {
            graphReference = GraphReference.New(flowMacro, ensureValid: true);
        }
        if (!graphReference.IsUnityNull() && graphReference.isValid)
        {
            GraphWindow.OpenActive(graphReference);
        }
    }

    [ContextMenu("Tranverse Graph")]
    void TraverseGraph()
    {
        if (!graphReference.IsUnityNull() && graphReference.isValid)
        {
            var so = graphReference.scriptableObject;
        }
        else
        {
            FlowGraph flowGraph = flowMacro.graph;
            foreach (var unit in flowGraph.units)
            {
                Debug.Log(unit);
            }
        }
    }
}
