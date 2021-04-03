using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Reflection;
using UnityEditor;

public class BoltExplorer : EditorWindow
{

    [MenuItem("Tool/Bolt Explorer")]
    static void Open()
    {
        var window = EditorWindow.CreateWindow<BoltExplorer>();
    }

    static Bolt.FlowMachine targetFlowMachine;
    static Bolt.FlowMachine[] flowMachines;
    static string filter = string.Empty;

    private class UnitData
    {
        public string hierachy;
        public Scene scene;
        public Bolt.FlowMachine flowMachine;
        public Bolt.FlowGraph flowGraph;
        public bool isInput;
        public Bolt.ValueInput valueInput;
        public object inputValue;
        public Bolt.ValueOutput valueOutput;
        public object outputValue;
    }

    private class SearchResult
    {
        public UnitData unitData;
    }

    static List<Bolt.FlowMachine> flowMachineList = new List<Bolt.FlowMachine>();
    static List<UnitData> unitList = new List<UnitData>();
    static List<SearchResult> searchResultList = new List<SearchResult>();

    void Collect()
    {
        GameObject[] gos = Resources.FindObjectsOfTypeAll<GameObject>();
        foreach (var go in gos)
        {
            var fms = go.GetComponentsInChildren<Bolt.FlowMachine>();
            Collect(fms);
        }
    }

    void Collect (Bolt.FlowMachine[] fms)
    {
        foreach (var f in fms)
        {
            Collect(f);
        }
    }

    void Collect(Bolt.FlowMachine fm)
    {
        if (fm == null)
            return;

        var hierachy = (fm.gameObject.scene.IsValid()? fm.gameObject.scene.name: "[Prefab]") + "/" + fm.name;
        flowMachineList.Add(fm);

        var graph = fm.graph;
        Collect(graph, fm, hierachy);
    }

    void Collect(Bolt.FlowGraph graph, Bolt.FlowMachine fm, string hierachy)
    {
        foreach (var unit in graph.units)
        {
            if (unit.GetType() == typeof(Bolt.SuperUnit))
            {
                var superUnit = unit as Bolt.SuperUnit;
                if (superUnit != null && superUnit.nest.graph != graph)
                    Collect(superUnit.nest.graph, fm, hierachy + "/" + superUnit.ToString());
            }
            foreach (var input in unit.inputs)
            {
                var valueInput = input as Bolt.ValueInput;
                if (valueInput != null)
                {
                    var t = valueInput.type;

                    // Collect string type only
                    if (t == typeof(string))
                    {
                        // Collect!
                        var unitData = new UnitData()
                        {
                            isInput = true,
                            valueInput = valueInput,
                            flowGraph = graph,
                            flowMachine = fm,
                            hierachy = hierachy,
                            scene = fm.gameObject.scene
                        };
                        unitList.Add(unitData);
                    }
                }
            }
            foreach (var output in unit.outputs)
            {
                var valueOutput = output as Bolt.ValueOutput;
                if (valueOutput != null)
                {
                    var t = valueOutput.type;

                    // Collect string type only
                    if (t == typeof(string))
                    {
                        // Collect!
                        var unitData = new UnitData()
                        {
                            isInput = false,
                            valueOutput = valueOutput,
                            flowGraph = graph,
                            flowMachine = fm,
                            hierachy = hierachy,
                            scene = fm.gameObject.scene
                        };
                        unitList.Add(unitData);
                    }
                }
            }
        }
    }

    private Vector2 scrollPosition;

    private void OnGUI()
    {

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        {
            targetFlowMachine = (Bolt.FlowMachine)EditorGUILayout.ObjectField(targetFlowMachine, typeof(Bolt.FlowMachine), true);
            EditorGUILayout.LabelField("Unit Count = " + unitList.Count);
            EditorGUILayout.LabelField("FlowMachine Count = " + flowMachineList.Count);

            if (GUILayout.Button("Collect"))
            {
                unitList.Clear();
                flowMachineList.Clear();
                if (targetFlowMachine)
                {
                    Collect(targetFlowMachine);
                }
                else
                {
                    Collect();
                }
            }
            filter = EditorGUILayout.TextField("Filter", filter);
            if (GUILayout.Button("Search"))
            {
                searchResultList.Clear();
                foreach (UnitData ud in unitList)
                {
                    var t = ud.isInput? ud.valueInput.GetType(): ud.valueOutput.GetType();
                    var isFound = false;
                    if (ud.isInput)
                    {
                        // Get Value input default value
                        PropertyInfo defaultValuePI = t.GetProperty("_defaultValue", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (ud.valueInput.hasDefaultValue)
                        {
                            if (defaultValuePI != null)
                            {
                                try
                                {
                                    var value = defaultValuePI.GetValue(ud.valueInput);
                                    ud.inputValue = value;
                                    if (string.IsNullOrEmpty(filter) || value.ToString().IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        isFound = true;
                                    }
                                }
                                catch (System.Exception e)
                                {
                                    Debug.LogException(e, ud.flowMachine);
                                }
                            }
                        }
                    }
                    else
                    {
                        // Get Value output value (function)
                        FieldInfo getValueFI = t.GetField("getValue", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (getValueFI != null)
                        {
                            var getValueFunc = (System.Func<Bolt.Flow, System.Object>)getValueFI.GetValue(ud.valueOutput);
                            var obj = getValueFunc.Target;
                            if (obj.GetType() == typeof(Bolt.Literal))
                            {
                                var literal = obj as Bolt.Literal;
                                var value = literal.value;
                                ud.outputValue = value;
                                if (string.IsNullOrEmpty(filter) || value.ToString().IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    isFound = true;
                                }
                            }
                        }
                    }

                    // Found!
                    if (isFound)
                    {
                        var sr = new SearchResult()
                        {
                            unitData = ud
                        };
                        searchResultList.Add(sr);
                    }
                }
            }

            foreach (var sr in searchResultList)
            {
                EditorGUILayout.BeginHorizontal();
                var fm = sr.unitData.flowMachine;
                var unit = sr.unitData.isInput? sr.unitData.valueInput.unit: sr.unitData.valueOutput.unit;
                var graph = sr.unitData.flowGraph;
                EditorGUILayout.LabelField(sr.unitData.hierachy);
                EditorGUILayout.ObjectField(fm, typeof(Bolt.FlowMachine), true);
                if (GUILayout.Button("Focus"))
                {
                    var gw = EditorWindow.GetWindow<Ludiq.GraphWindow>();
                    Selection.activeObject = fm;
                    graph.pan = unit.position;
                }
                var portName = sr.unitData.isInput ? "> " + sr.unitData.valueInput.key : "< " + sr.unitData.valueOutput.key;
                var valueContent = sr.unitData.isInput ? sr.unitData.inputValue as string : sr.unitData.outputValue as string;
                EditorGUILayout.LabelField(string.Format("{0} = {1}", portName, valueContent));
                EditorGUILayout.EndHorizontal();
            }
        }
        EditorGUILayout.EndScrollView();

    }
}
