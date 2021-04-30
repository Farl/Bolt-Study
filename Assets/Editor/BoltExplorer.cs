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

    private class Navigation
    {
        public int pageLimit = 50;
        private int currPage = 0;
        private int refCount = 0;

        public void ResetPage()
        {
            currPage = 0;
        }

        public void StartNavigation()
        {
            refCount = 0;
        }

        public bool CheckNavigation()
        {
            if (refCount < currPage * pageLimit || refCount >= (currPage + 1) * pageLimit)
            {
                refCount++;
                return false;
            }
            else
            {
                refCount++;
                return true;
            }
        }

        public void EndNavigation()
        {
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
    }

    static MonoBehaviour targetMonoBehaviour;
    static MonoBehaviour[] monoBehaviours;
    static string filter = string.Empty;
    static Navigation n = new Navigation();

    private class UnitData
    {
        public string hierachy;
        public Scene scene;
        public MonoBehaviour monoBehaviour;
        public FlowMacro flowMacro;
        public FlowGraph flowGraph;
        public IUnit unit;
        public SuperUnit superUnit;

        public List<PortData> portDatas = new List<PortData>();
    }

    private class PortData
    {
        public PropertyInfo pi;
        public bool isInput;
        public ValueInput valueInput;
        public object inputValue;
        public ValueOutput valueOutput;
        public object outputValue;

        public FieldInfo fi;
        public object target;

        public ValueInputDefinition vid;
    }

    private class SearchResult
    {
        public UnitData unitData;
        public HashSet<int> matchPorts = new HashSet<int>();
    }

    static List<MonoBehaviour> monoBehaviourList = new List<MonoBehaviour>();
    static List<UnitData> unitList = new List<UnitData>();
    static Dictionary<IUnit, SearchResult> searchResultMap = new Dictionary<IUnit, SearchResult>();

    private enum Mode
    {
        Scene = 0,
        Project = 1,
        FlowMacro = 2,
        Selection = 3
    }
    static Mode mode = Mode.Scene;

    #region Collect
    void Collect()
    {
        switch (mode)
        {
            case Mode.Scene:
            case Mode.Project:
                var gos = Resources.FindObjectsOfTypeAll<GameObject>();
                foreach (var go in gos)
                {
                    if ((mode == Mode.Scene) == go.scene.IsValid())
                    {
                        var fms = go.GetComponentsInChildren<MonoBehaviour>(true);
                        Collect(fms);
                    }
                }
                break;

            case Mode.FlowMacro:
                var guids = AssetDatabase.FindAssets("t:FlowMacro");
                foreach (var guid in guids)
                {
                    var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    var flowMacro = AssetDatabase.LoadAssetAtPath<FlowMacro>(assetPath);
                    if (flowMacro)
                        Collect(flowMacro.graph, null, flowMacro, assetPath);
                }
                break;

            case Mode.Selection:
                gos = Selection.gameObjects;
                foreach (var go in gos)
                {
                    var fms = go.GetComponentsInChildren<MonoBehaviour>(true);
                    Collect(fms);
                }
                break;
            default:
                break;
        }
    }

    void Collect(MonoBehaviour[] mbs)
    {
        if (mbs == null)
            return;
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

    void Collect(FlowGraph graph, MonoBehaviour mb, FlowMacro flowMacro, string hierachy, SuperUnit inSuperUnit = null)
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
                scene = (mb != null) ? mb.gameObject.scene : new Scene(),
                superUnit = inSuperUnit
            };
            unitList.Add(unitData);

            // Traverse into SuperUnit
            if (unit.GetType() == typeof(SuperUnit))
            {
                var superUnit = unit as SuperUnit;
                if (superUnit != null && superUnit.nest.graph != graph)
                {
                    // Collect recursive
                    Collect(superUnit.nest.graph, mb, flowMacro, hierachy + "/" + superUnit.ToString(), (inSuperUnit.IsUnityNull() ? superUnit : inSuperUnit));

                    // SuperUnit input (default value)
                    foreach (var vid in superUnit.nest.graph.valueInputDefinitions)
                    {
                        if (vid.type == typeof(string) && vid.hasDefaultValue)
                        {
                            // Collect
                            var portData = new PortData()
                            {
                                vid = vid
                            };
                            unitData.portDatas.Add(portData);
                        }
                    }
                }

            }

            var unitType = unit.GetType();
            PropertyInfo[] pis = unitType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var pi in pis)
            {
                if (pi.PropertyType == typeof(ValueInput))
                {
                    ValueInput vi = pi.GetValue(unit) as ValueInput;
                    // Collect string type only
                    if (!vi.IsUnityNull() && vi.type == typeof(string))
                    {
                        // Collect!
                        var portData = new PortData()
                        {
                            pi = pi,
                            isInput = true,
                            valueInput = vi,
                        };
                        unitData.portDatas.Add(portData);
                    }
                }
                else if (pi.PropertyType == typeof(ValueOutput))
                {
                    ValueOutput vo = pi.GetValue(unit) as ValueOutput;
                    // Collect string type only
                    if (!vo.IsUnityNull() && vo.type == typeof(string))
                    {
                        // Collect!
                        var portData = new PortData()
                        {
                            pi = pi,
                            isInput = false,
                            valueOutput = vo,
                        };
                        unitData.portDatas.Add(portData);
                    }
                }
            }
            FieldInfo[] fis = unitType.GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (var fi in fis)
            {
                if (fi.FieldType == typeof(string))
                {
                    var ins = fi.GetCustomAttribute<InspectableAttribute>();
                    var head = fi.GetCustomAttribute<UnitHeaderInspectableAttribute>();
                    if (ins != null || head != null)
                    {
                        // Collect!
                        var portData = new PortData()
                        {
                            fi = fi,
                            target = unit
                        };
                        unitData.portDatas.Add(portData);
                    }
                }
            }
        }
    }
    #endregion

    private Vector2 scrollPosition;

    #region Search
    private void AddSearchResult(UnitData unitData, int portIndex = -1)
    {
        var unit = unitData.unit;
        if (unit.IsUnityNull())
            return;

        SearchResult searchResult;
        if (searchResultMap.ContainsKey(unit))
        {
            searchResult = searchResultMap[unit];
        }
        else
        {
            searchResult = new SearchResult()
            {
                unitData = unitData,
            };
            searchResultMap.Add(unit, searchResult);
        }
        if (searchResult != null)
        {
            if (!searchResult.matchPorts.Contains(portIndex))
                searchResult.matchPorts.Add(portIndex);
        }
    }

    private void Search()
    {
        n.ResetPage();
        searchResultMap.Clear();
        foreach (UnitData ud in unitList)
        {
            var unit = ud.unit;

            // Match unit name
            var uid = unit.ToString();
            var collectAllName = uid.Substring(0, uid.IndexOf('#'));
            var unitType = unit.GetType();
            var ta = unitType.GetAttribute<UnitShortTitleAttribute>();
            if (ta != null)
            {
                collectAllName += "," + ta.title;
            }
            var sta = unitType.GetAttribute<UnitTitleAttribute>();
            if (sta != null)
            {
                collectAllName += "," + sta.title;
            }
            if (unitType == typeof(SuperUnit))
            {
                var su = unit as SuperUnit;
                if (!su.nest.graph.IsUnityNull())
                {
                    collectAllName += "," + su.nest.graph.title;
                }
            }
            if (collectAllName.Replace(" ", string.Empty).IndexOf(filter.Replace(" ", string.Empty), System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                AddSearchResult(ud);
            }

            for (int i = 0; i < ud.portDatas.Count; i++)
            {
                var pd = ud.portDatas[i];

                if (!pd.vid.IsUnityNull())
                {
                    var su = unit as SuperUnit;
                    if (su.defaultValues.TryGetValue(pd.vid.key, out object value))
                    {
                        if (string.IsNullOrEmpty(filter) || (value != null && value.ToString().IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            AddSearchResult(ud, i);
                        }
                    }
                }
                else if (pd.fi != null)
                {
                    var value = pd.fi.GetValue(unit);
                    if (string.IsNullOrEmpty(filter) || (value != null && value.ToString().IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        AddSearchResult(ud, i);
                    }
                }
                else if (pd.isInput)
                {
                    var t = pd.isInput ? pd.valueInput.GetType() : pd.valueOutput.GetType();

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
                                    AddSearchResult(ud, i);
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
                    var t = pd.isInput ? pd.valueInput.GetType() : pd.valueOutput.GetType();

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
                                AddSearchResult(ud, i);
                            }
                        }
                    }
                }
            }
        }
    }
    #endregion

    static GUIStyle hierachyStyle;

    private void OnGUI()
    {
        if (hierachyStyle == null)
        {
            hierachyStyle = new GUIStyle();
            hierachyStyle.fontSize = 10;

        }
        EditorGUILayout.LabelField("Unit Count = " + unitList.Count);
        EditorGUILayout.LabelField("MonoBehaviour Count = " + monoBehaviourList.Count);

        mode = (Mode)EditorGUILayout.EnumPopup(mode);

        if (GUILayout.Button("Collect"))
        {
            unitList.Clear();
            monoBehaviourList.Clear();
            searchResultMap.Clear();
            Collect();
            Search();
        }

        EditorGUI.BeginChangeCheck();
        filter = EditorGUILayout.DelayedTextField("Filter", filter);
        if (EditorGUI.EndChangeCheck())
        {
            Search();
        }

        // Navigation
        n.pageLimit = EditorGUILayout.IntField("Page Limit", n.pageLimit);
        n.StartNavigation();
        GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(4));

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        foreach (var kvp in searchResultMap)
        {
            var sr = kvp.Value;

            // Navigation
            if (!n.CheckNavigation())
            {
                continue;
            }

            var unitData = sr.unitData;
            var mb = unitData.monoBehaviour;
            var unit = unitData.unit;
            var graph = unitData.flowGraph;
            var flowMacro = unitData.flowMacro;

            EditorGUILayout.LabelField(sr.unitData.hierachy, hierachyStyle);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(unit.ToSafeString(), GUILayout.Width(200)))
            {
                if (mb)
                    Selection.activeObject = mb;

                if (flowMacro.IsUnityNull())
                {
                    GraphWindow.OpenTab();
                }
                else
                {
                    if (!unitData.superUnit.IsUnityNull())
                    {
                        var graphRef = GraphReference.New(flowMacro, true);
                        var childGraphRef = graphRef.ChildReference(unitData.superUnit, false);
                        GraphWindow.OpenTab(childGraphRef);
                    }
                    else
                    {
                        GraphWindow.OpenActive(GraphReference.New(flowMacro, true));
                    }
                }
                graph.pan = unit.position;
            }
            if (mb)
            {
                EditorGUILayout.ObjectField(mb, typeof(MonoBehaviour), true, GUILayout.Width(150));
            }
            if (flowMacro)
            {
                EditorGUILayout.ObjectField(flowMacro, typeof(FlowMacro), true, GUILayout.Width(150));
            }
            EditorGUILayout.EndHorizontal();

            // Matched ports
            EditorGUILayout.BeginVertical();
            foreach (var idx in sr.matchPorts)
            {
                var pd = idx >= 0 ? sr.unitData.portDatas[idx] : null;
                if (pd != null)
                {
                    var portName = "";
                    string valueContent = string.Empty;

                    if (!pd.vid.IsUnityNull())
                    {
                        portName = string.IsNullOrEmpty(pd.vid.label)? pd.vid.key: pd.vid.label;
                        var su = unit as SuperUnit;
                        if (su.defaultValues.TryGetValue(pd.vid.key, out object dv))
                        {
                            valueContent = dv as string;
                        }
                    }
                    else if (pd.fi != null)
                    {
                        portName = pd.fi.DisplayName();
                        valueContent = pd.fi.GetValue(pd.target) as string;
                    }
                    else
                    {
                        portName = pd.isInput ? pd.valueInput.key : pd.valueOutput.key;
                        if (pd.pi != null)
                        {
                            var pl = pd.pi.GetCustomAttribute<PortLabelAttribute>();
                            if (pl != null)
                                portName = pl.label;
                        }
                        valueContent = pd.isInput ? pd.inputValue as string : pd.outputValue as string;
                    }

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField((pd.isInput? "[IN]": "[OUT]") + $" {portName}", GUILayout.Width(150));
                    EditorGUILayout.TextField(valueContent, GUILayout.Width(200));
                    EditorGUILayout.EndHorizontal();
                }
            }

            GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(4));
            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.EndScrollView();

        // End Navigation
        n.EndNavigation();
    }
}
