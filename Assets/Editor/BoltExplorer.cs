using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.Reflection;

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
        public Bolt.FlowMachine flowMachine;
        public Bolt.FlowGraph flowGraph;
        public Bolt.ValueInput valueInput;
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
        flowMachineList.Add(fm);

        var graph = fm.graph;
        foreach (var unit in graph.units)
        {
            foreach (var input in unit.inputs)
            {
                //Debug.Log("In " + input.ToString());
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
                            valueInput = valueInput,
                            flowGraph = graph,
                            flowMachine = fm
                        };
                        unitList.Add(unitData);
                    }
                    /*
                    FieldInfo[] fis = t.GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
                    if (fis != null)
                    {
                        foreach (FieldInfo fi in fis)
                        {
                            Debug.Log("  Field = " + fi.Name);
                        }
                    }
                    PropertyInfo[] pis = t.GetProperties(BindingFlags.NonPublic | BindingFlags.Instance);
                    if (pis != null)
                    {
                        foreach (PropertyInfo pi in pis)
                        {
                            Debug.Log("  Property = " + pi.Name);
                        }
                    }
                    MethodInfo[] mis = t.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance);
                    if (mis != null)
                    {
                        foreach (MethodInfo mi in mis)
                        {
                            Debug.Log("  Method = " + mi.Name);
                        }
                    }
                    */
                }
            }
            foreach (var output in unit.outputs)
            {
                //Debug.Log("Out " + output.ToString());
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
                    var t = ud.valueInput.GetType();

                    PropertyInfo defaultValuePI = t.GetProperty("_defaultValue", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (defaultValuePI != null)
                    {
                        var value = defaultValuePI.GetValue(ud.valueInput);
                        if (string.IsNullOrEmpty(filter) || value.ToString().IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            // Found!
                            var sr = new SearchResult()
                            {
                                unitData = ud
                            };
                            searchResultList.Add(sr);
                        }
                    }
                }
            }

            foreach (var sr in searchResultList)
            {
                var fm = sr.unitData.flowMachine;
                var unit = sr.unitData.valueInput.unit;
                var graph = sr.unitData.flowGraph;
                EditorGUILayout.ObjectField(fm, typeof(Bolt.FlowMachine), true);
                if (GUILayout.Button("Focus"))
                {
                    var gw = EditorWindow.GetWindow<Ludiq.GraphWindow>();
                    Selection.activeObject = fm;
                    graph.pan = unit.position;
                }
                EditorGUILayout.LabelField(sr.unitData.valueInput.key);
            }

            if (GUILayout.Button("Test"))
            {
                Debug.Log("");


            }
            if (GUILayout.Button("Test2"))
            {
                var gw = EditorWindow.GetWindow<Ludiq.GraphWindow>();
                PropertyInfo pi = gw.GetType().GetProperty("graph", BindingFlags.NonPublic | BindingFlags.Instance);
                if (pi != null)
                {
                    gw.Repaint();
                }
                Debug.Log(gw.titleContent.text);
            }
        }
        EditorGUILayout.EndScrollView();

    }
}
