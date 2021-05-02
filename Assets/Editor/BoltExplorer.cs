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

        public int RefCount
        {
            get
            { return refCount; }
        }

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
            pageLimit = EditorGUILayout.IntField("Page Content Limit", pageLimit);
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

    static string filter = string.Empty;
    static Navigation n = new Navigation();

    private class UnitData
    {
        public string hierachy;
        public Scene? scene
        {
            get { return monoBehaviour?.gameObject?.scene; }
        }
        public MonoBehaviour monoBehaviour;
        public FlowMacro flowMacro;
        public FlowGraph flowGraph;
        public IUnit unit;
        public SuperUnit superUnit;

        public List<PortData> portDatas = new List<PortData>();

        public void AddPortData(PropertyInfo pi, IUnitPort unitPort)
        {
            PortData pd = new PortData()
            {
                memberInfo = pi,
                unitPort = unitPort
            };
            portDatas.Add(pd);
        }

        public void AddPortData(FieldInfo fi)
        {
            PortData pd = new PortData()
            {
                memberInfo = fi
            };
            portDatas.Add(pd);
        }

        public void AddPortData(ValueInputDefinition vid)
        {
            PortData pd = new PortData()
            {
                vid = vid
            };
            portDatas.Add(pd);
        }
    }

    private class PortData
    {

        public ValueInputDefinition vid;
        public MemberInfo memberInfo;
        public IUnitPort unitPort;
        public object valueCache;

        public bool isInput
        {
            get
            {
                return !valueInput.IsUnityNull();
            }
        }
        public ValueInput valueInput
        {
            get
            {
                return unitPort as ValueInput;
            }
        }
        public ValueOutput valueOutput
        {
            get
            {
                return unitPort as ValueOutput;
            }
        }
        public string GetPortName()
        {
            var portName = string.Empty;

            if (!vid.IsUnityNull())
            {
                portName = string.IsNullOrEmpty(vid.label) ? vid.key : vid.label;
            }
            else if ((memberInfo as FieldInfo) != null)
            {
                portName = memberInfo.DisplayName();
                var header = memberInfo.GetCustomAttribute<UnitHeaderInspectableAttribute>();
                if (header != null && !string.IsNullOrEmpty(header.label))
                    portName = header.label;

            }
            else if (memberInfo as PropertyInfo != null)
            {
                portName = memberInfo.DisplayName();
                var pl = memberInfo.GetCustomAttribute<PortLabelAttribute>();
                if (pl != null)
                    portName = pl.label;
            }
            return portName;
        }
        public T GetValueCache<T>()
        {
            return (T)valueCache;
        }
        public T GetValue<T>(IUnit unit)
        {
            object value = null;
            if (!vid.IsUnityNull())
            {
                var su = unit as SuperUnit;
                su.defaultValues.TryGetValue(vid.key, out value);
            }
            else if ((memberInfo as FieldInfo) != null)
            {
                var fi = memberInfo as FieldInfo;
                value = fi.GetValue(unit);
            }
            else if ((memberInfo as PropertyInfo) != null && isInput)
            {
                var valueInput = unitPort as ValueInput;
                var t = typeof(ValueInput);

                // Get Value input default value
                PropertyInfo defaultValuePI = t.GetProperty("_defaultValue", BindingFlags.NonPublic | BindingFlags.Instance);
                if (valueInput.hasDefaultValue && defaultValuePI != null)
                {
                    try
                    {
                        value = defaultValuePI.GetValue(valueInput);
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
            }
            else if ((memberInfo as PropertyInfo) != null && !isInput)
            {
                var valueOutput = unitPort as ValueOutput;
                var t = typeof(ValueOutput);

                // Get Value output value (function)
                FieldInfo getValueFI = t.GetField("getValue", BindingFlags.NonPublic | BindingFlags.Instance);
                if (getValueFI != null)
                {
                    var getValueFunc = (System.Func<Flow, System.Object>)getValueFI.GetValue(valueOutput);
                    var obj = getValueFunc.Target;
                    if (obj.GetType() == typeof(Literal))
                    {
                        var literal = obj as Literal;
                        value = literal.value;
                    }
                }
            }
            valueCache = value;
            return (T)value;
        }
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
    void Collect<T>()
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
                        var mbs = go.GetComponentsInChildren<MonoBehaviour>(true);
                        Collect<T>(mbs);
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
                        Collect<T>(flowMacro.graph, null, flowMacro, assetPath);
                }
                break;

            case Mode.Selection:
                gos = Selection.gameObjects;
                foreach (var go in gos)
                {
                    var mbs = go.GetComponentsInChildren<MonoBehaviour>(true);
                    Collect<T>(mbs);
                }
                break;
            default:
                break;
        }
    }

    void Collect<T>(MonoBehaviour[] mbs)
    {
        if (mbs == null)
            return;
        foreach (var f in mbs)
        {
            Collect<T>(f);
        }
    }

    void Collect<T>(MonoBehaviour mb)
    {
        if (mb == null)
            return;

        var hierachy = (mb.gameObject.scene.IsValid() ? $@"<b>[Scene]</b> {mb.gameObject.scene.name} " : @" ");
        if (mb != null)
        {
            hierachy += $@"<b>[GameObject]</b> {mb.name}";
        }

        FlowGraph graph;

        // Find FlowMacro
        var mbType = mb.GetType();

        if (mbType == typeof(FlowMachine))
        {
            var fm = mb as FlowMachine;
            graph = fm.graph;
            Collect<T>(graph, fm, fm.nest.macro, hierachy);
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
                        Collect<T>(graph, mb, flowMacro, hierachy);
                    }
                }
            }
        }
    }

    void Collect<T>(FlowGraph graph, MonoBehaviour mb, FlowMacro flowMacro, string hierachy, SuperUnit inSuperUnit = null)
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
                superUnit = inSuperUnit
            };
            unitList.Add(unitData);

            // Traverse into SuperUnit
            if (unit.GetType() == typeof(SuperUnit))
            {
                var superUnit = unit as SuperUnit;
                var nestGraph = superUnit.nest.graph;
                if (!superUnit.IsUnityNull() && !nestGraph.IsUnityNull() && nestGraph != graph)
                {
                    // Collect recursive
                    var superUnitName = (!string.IsNullOrEmpty(nestGraph.title)) ? nestGraph.title : superUnit.ToString();
                    var newHierachy = $@"{hierachy} [<color=blue>{superUnitName}</color>]";
                    Collect<T>(nestGraph, mb, flowMacro, newHierachy, superUnit);

                    // SuperUnit input (default value)
                    foreach (var vid in nestGraph.valueInputDefinitions)
                    {
                        if (vid.type == typeof(T) && vid.hasDefaultValue)
                        {
                            // Collect
                            unitData.AddPortData(vid);
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
                    // Collect specify type only
                    if (!vi.IsUnityNull() && vi.type == typeof(T))
                    {
                        // Collect!
                        unitData.AddPortData(pi, vi);
                    }
                }
                else if (pi.PropertyType == typeof(ValueOutput))
                {
                    ValueOutput vo = pi.GetValue(unit) as ValueOutput;
                    // Collect specify type only
                    if (!vo.IsUnityNull() && vo.type == typeof(T))
                    {
                        // Collect!
                        unitData.AddPortData(pi, vo);
                    }
                }
            }
            FieldInfo[] fis = unitType.GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (var fi in fis)
            {
                // Collect specify type only
                if (fi.FieldType == typeof(T))
                {
                    var ins = fi.GetCustomAttribute<InspectableAttribute>();
                    var head = fi.GetCustomAttribute<UnitHeaderInspectableAttribute>();
                    if (ins != null || head != null)
                    {
                        // Collect!
                        unitData.AddPortData(fi);
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
        scrollPosition = Vector2.zero;
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

                var value = pd.GetValue<string>(unit);
                if (string.IsNullOrEmpty(filter) || (value != null && value.ToString().IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    AddSearchResult(ud, i);
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
            hierachyStyle.fontSize = 12;

        }

        EditorGUILayout.BeginHorizontal();
        mode = (Mode)EditorGUILayout.EnumPopup(mode, GUILayout.Width(100));

        if (GUILayout.Button("Collect", GUILayout.Width(100)))
        {
            unitList.Clear();
            searchResultMap.Clear();
            Collect<string>();
            Search();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUI.BeginChangeCheck();

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Filter", GUILayout.Width(100));
        filter = EditorGUILayout.DelayedTextField(filter);
        EditorGUILayout.EndHorizontal();

        if (EditorGUI.EndChangeCheck())
        {
            Search();
        }
        EditorGUILayout.LabelField($@"Unit Count = {searchResultMap.Count} / {unitList.Count}");

        // Navigation
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

            EditorGUILayout.LabelField($@"<b>[{n.RefCount}]</b> {sr.unitData.hierachy}", hierachyStyle);
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
                        // Open SuperUnit
                        var graphRef = GraphReference.New(flowMacro, true);

                        var hierachyUnits = new List<SuperUnit>();

                        var currUnitData = unitData;
                        while (currUnitData != null && !currUnitData.superUnit.IsUnityNull())
                        {
                            hierachyUnits.Insert(0, currUnitData.superUnit);
                            currUnitData = unitList.Find(x => x.unit == currUnitData.superUnit);
                        }
                        for (int i = 0; i < hierachyUnits.Count; i++)
                        {
                            var u = hierachyUnits[i];
                            Debug.Log($"{u.ToString()} { u.nest.graph.title}");
                            var currGraphRef = graphRef.ChildReference(u, false);

                            if (!currGraphRef.IsUnityNull())
                            {
                                graphRef = currGraphRef;
                            }
                        }
                        GraphWindow.OpenActive(graphRef);
                    }
                    else
                    {
                        // Open Macro
                        GraphWindow.OpenActive(GraphReference.New(flowMacro, true));
                    }
                }
                // Pan
                graph.pan = unit.position;
            }
            if (mb)
            {
                EditorGUILayout.ObjectField(mb, typeof(MonoBehaviour), true, GUILayout.Width(200));
            }
            if (flowMacro)
            {
                EditorGUILayout.ObjectField(flowMacro, typeof(FlowMacro), true, GUILayout.Width(200));
            }
            EditorGUILayout.EndHorizontal();

            // Matched ports
            EditorGUILayout.BeginVertical();
            foreach (var idx in sr.matchPorts)
            {
                var pd = idx >= 0 ? sr.unitData.portDatas[idx] : null;
                if (pd != null)
                {
                    var portName = pd.GetPortName();
                    string valueContent = pd.GetValueCache<string>();

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"{portName}", GUILayout.Width(200));
                    EditorGUILayout.TextField(valueContent, GUILayout.Width(200));
                    EditorGUILayout.EndHorizontal();
                }
            }

            GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(4));
            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.EndScrollView();

        GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(4));

        // End Navigation
        n.EndNavigation();
    }
}
