using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Reflection;
using UnityEditor;
using Ludiq;
using Bolt;

public class BoltExplorer : EditorWindow
{

    [MenuItem("JetGen/Bolt/Bolt Explorer")]
    static void Open()
    {
        var window = EditorWindow.CreateWindow<BoltExplorer>();
    }

    static MonoBehaviour targetMonoBehaviour;
    static MonoBehaviour[] monoBehaviours;
    static string filter = string.Empty;
    static int pageLimit = 50;
    static int currPage = 0;

    private class UnitData
    {
        public string hierachy;
        public Scene scene;
        public MonoBehaviour monoBehaviour;
        public FlowMacro flowMacro;
        public FlowGraph flowGraph;
        public IUnit unit;

        public List<PortData> portDatas = new List<PortData>();
    }

    private class PortData
    {
        public bool isInput;
        public ValueInput valueInput;
        public object inputValue;
        public ValueOutput valueOutput;
        public object outputValue;
    }

    private class SearchResult
    {
        public UnitData unitData;
        public int portIndex = -1;
    }

    static List<MonoBehaviour> monoBehaviourList = new List<MonoBehaviour>();
    static List<UnitData> unitList = new List<UnitData>();
    static List<SearchResult> searchResultList = new List<SearchResult>();

    private enum Mode
    {
        Scene = 0,
        Project = 1
    }
    static Mode mode = Mode.Scene;

    void Collect()
    {
        GameObject[] gos = Resources.FindObjectsOfTypeAll<GameObject>();
        foreach (var go in gos)
        {
            if ((mode == Mode.Scene) == go.scene.IsValid())
            {
                var fms = go.GetComponentsInChildren<MonoBehaviour>();
                Collect(fms);
            }
        }
    }

    void Collect(MonoBehaviour[] mbs)
    {
        foreach (var f in mbs)
        {
            Collect(f);
        }
    }

    void Collect(MonoBehaviour mb)
    {
        if (mb == null)
            return;

        var hierachy = (mb.gameObject.scene.IsValid() ? mb.gameObject.scene.name : "[Prefab]") + "/" + mb.name;

        FlowGraph graph;

        // Find FlowMacro
        var mbType = mb.GetType();

        if (mbType == typeof(FlowMachine))
        {
            var fm = mb as FlowMachine;
            graph = fm.graph;
            Collect(graph, fm, fm.nest.macro, hierachy);
        }
        else
        {
            FieldInfo[] fis = mbType.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            foreach (var fi in fis)
            {
                var fType = fi.FieldType;
                if (fType == typeof(FlowMacro))
                {
                    var flowMacro = fi.GetValue(mb) as FlowMacro;
                    if (flowMacro != null && flowMacro.graph != null)
                    {
                        graph = flowMacro.graph;
                        monoBehaviourList.Add(mb);
                        Collect(graph, mb, flowMacro, hierachy);
                    }
                }
            }
        }
    }

    void Collect(FlowGraph graph, MonoBehaviour mb, FlowMacro flowMacro, string hierachy)
    {
        if (graph.IsUnityNull())
            return;

        foreach (var unit in graph.units)
        {
            var unitData = new UnitData()
            {
                unit = unit,
                flowGraph = graph,
                flowMacro = flowMacro,
                monoBehaviour = mb,
                hierachy = hierachy,
                scene = mb.gameObject.scene
            };
            unitList.Add(unitData);

            // Traverse into SuperUnit
            if (unit.GetType() == typeof(SuperUnit))
            {
                var superUnit = unit as SuperUnit;
                if (superUnit != null && superUnit.nest.graph != graph)
                    Collect(superUnit.nest.graph, mb, flowMacro, hierachy + "/" + superUnit.ToString());
            }
            foreach (var input in unit.inputs)
            {
                var valueInput = input as ValueInput;
                if (valueInput != null)
                {
                    var t = valueInput.type;

                    // Collect string type only
                    if (t == typeof(string))
                    {
                        // Collect!
                        var portData = new PortData()
                        {
                            isInput = true,
                            valueInput = valueInput,
                        };
                        unitData.portDatas.Add(portData);
                    }
                }
            }
            foreach (var output in unit.outputs)
            {
                var valueOutput = output as ValueOutput;
                if (valueOutput != null)
                {
                    var t = valueOutput.type;

                    // Collect string type only
                    if (t == typeof(string))
                    {
                        // Collect!
                        var portData = new PortData()
                        {
                            isInput = false,
                            valueOutput = valueOutput,
                        };
                        unitData.portDatas.Add(portData);
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
            targetMonoBehaviour = (FlowMachine)EditorGUILayout.ObjectField(targetMonoBehaviour, typeof(FlowMachine), true);
            EditorGUILayout.LabelField("Unit Count = " + unitList.Count);
            EditorGUILayout.LabelField("MonoBehaviour Count = " + monoBehaviourList.Count);

            mode = (Mode)EditorGUILayout.EnumPopup(mode);

            if (GUILayout.Button("Collect"))
            {
                unitList.Clear();
                monoBehaviourList.Clear();
                if (targetMonoBehaviour)
                {
                    Collect(targetMonoBehaviour);
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

                    // Match unit name
                    if (ud.unit.ToString().IndexOf(filter.Replace(" ", string.Empty), System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var sr = new SearchResult()
                        {
                            unitData = ud
                        };
                        searchResultList.Add(sr);
                    }

                    for (int i = 0; i < ud.portDatas.Count; i++)
                    {
                        var pd = ud.portDatas[i];

                        var t = pd.isInput ? pd.valueInput.GetType() : pd.valueOutput.GetType();

                        if (pd.isInput)
                        {
                            // Get Value input default value
                            PropertyInfo defaultValuePI = t.GetProperty("_defaultValue", BindingFlags.NonPublic | BindingFlags.Instance);
                            if (pd.valueInput.hasDefaultValue)
                            {
                                if (defaultValuePI != null)
                                {
                                    try
                                    {
                                        var value = defaultValuePI.GetValue(pd.valueInput);
                                        pd.inputValue = value;
                                        if (string.IsNullOrEmpty(filter) || (value != null && value.ToString().IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0))
                                        {
                                            var sr = new SearchResult()
                                            {
                                                unitData = ud,
                                                portIndex = i
                                            };
                                            searchResultList.Add(sr);
                                        }
                                    }
                                    catch (System.Exception e)
                                    {
                                        Debug.LogException(e, ud.monoBehaviour);
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
                                var getValueFunc = (System.Func<Flow, System.Object>)getValueFI.GetValue(pd.valueOutput);
                                var obj = getValueFunc.Target;
                                if (obj.GetType() == typeof(Literal))
                                {
                                    var literal = obj as Literal;
                                    var value = literal.value;
                                    pd.outputValue = value;
                                    if (string.IsNullOrEmpty(filter) || value.ToString().IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        var sr = new SearchResult()
                                        {
                                            unitData = ud,
                                            portIndex = i
                                        };
                                        searchResultList.Add(sr);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            pageLimit = EditorGUILayout.IntField("Page Limit", pageLimit);
            int refCount = 0;

            foreach (var sr in searchResultList)
            {
                if (refCount < currPage * pageLimit || refCount >= (currPage + 1) * pageLimit)
                {
                    refCount++;
                    continue;
                }
                else
                {
                    refCount++;
                }

                EditorGUILayout.BeginHorizontal();
                var fm = sr.unitData.monoBehaviour;
                var unit = sr.unitData.unit;
                var graph = sr.unitData.flowGraph;
                var flowMacro = sr.unitData.flowMacro;

                EditorGUILayout.LabelField(sr.unitData.hierachy);
                EditorGUILayout.ObjectField(fm, typeof(FlowMachine), true);
                if (GUILayout.Button("Focus"))
                {
                    //var gw = EditorWindow.GetWindow<Ludiq.GraphWindow>();
                    Selection.activeObject = fm;
                    GraphWindow.OpenActive(GraphReference.New(flowMacro, true));
                    graph.pan = unit.position;
                }

                var pd = sr.portIndex >= 0 ? sr.unitData.portDatas[sr.portIndex] : null;
                if (pd != null)
                {
                    var portName = pd.isInput ? "> " + pd.valueInput.key : "< " + pd.valueOutput.key;
                    var valueContent = pd.isInput ? pd.inputValue as string : pd.outputValue as string;
                    EditorGUILayout.LabelField(string.Format("{0} = {1}", portName, valueContent));
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            {
                if (GUILayout.Button("|<"))
                {
                    currPage = 0;
                }
                if (GUILayout.Button("<"))
                {
                    if (currPage <= 0)
                    {
                        currPage = 0;
                    }
                    else
                    {
                        currPage--;
                    }
                }
                EditorGUILayout.LabelField(string.Format("Page = {0} / {1}", currPage + 1, 1 + (refCount - 1) / pageLimit));
                if (GUILayout.Button(">"))
                {
                    if (currPage >= (refCount - 1) / pageLimit)
                    {
                        currPage = ((refCount - 1) / pageLimit);
                    }
                    else
                    {
                        currPage++;
                    }
                }
                if (GUILayout.Button(">|"))
                {
                    currPage = ((refCount - 1) / pageLimit);
                }
            }
            EditorGUILayout.EndHorizontal();
        }


        EditorGUILayout.EndScrollView();
    }
}
