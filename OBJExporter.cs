#if UNITY_EDITOR
using UnityEngine;
using System.Collections;
using UnityEditor;
using System.Text;
using System.Collections.Generic;
using System.IO;

/*=============================================================================
 |	    Project:  Unity3D Scene OBJ Exporter
 |
 |		  Notes: Only works with meshes + meshRenderers. No terrain yet
 |
 |       Author:  aaro4130
 |
 |     DO NOT USE PARTS OF THIS CODE, OR THIS CODE AS A WHOLE AND CLAIM IT
 |     AS YOUR OWN WORK. USE OF CODE IS ALLOWED IF I (aaro4130) AM CREDITED
 |     FOR THE USED PARTS OF THE CODE.
 |
 *===========================================================================*/
/**
 * 格式
 * poly[0] v, vn, vt
 * poly[1] v, vn, vt
 * poly[2] v, vn, vt
 * poly[3] v, vn, vt 可以有也可以没有
 */
public class polygon
{
    public List<triples> poly;
    public polygon()
    {
        poly = new List<triples>();
    }
    public int getV(int index)
    {
        return poly[index % poly.Count].v;
    }
    public int getVN(int index)
    {
        return poly[index % poly.Count].vn;
    }
    public int getVT(int index)
    {
        return poly[index % poly.Count].vt;
    }
}

public class triples
{
    public int v;
    public int vn;
    public int vt;
    public triples(int _v, int _vn, int _vt)
    {
        v = _v;
        vn = _vn;
        vt = _vt;
    }
}

public class OBJExporter : ScriptableWizard
{
    public bool onlySelectedObjects = false;
    public bool applyPosition = true;
    public bool applyRotation = true;
    public bool applyScale = true;
    public bool generateMaterials = false;
    public bool exportTextures = false;
    public bool splitObjects = true;
    public bool autoMarkTexReadable = false;
    public bool objNameAddIdNum = false;

    //public bool materialsUseTextureName = false;

    private string versionString = "v2.0";
    private string lastExportFolder;

    bool StaticBatchingEnabled()
    {
        PlayerSettings[] playerSettings = Resources.FindObjectsOfTypeAll<PlayerSettings>();
        if (playerSettings == null)
        {
            return false;
        }
        SerializedObject playerSettingsSerializedObject = new SerializedObject(playerSettings);
        SerializedProperty batchingSettings = playerSettingsSerializedObject.FindProperty("m_BuildTargetBatching");
        for (int i = 0; i < batchingSettings.arraySize; i++)
        {
            SerializedProperty batchingArrayValue = batchingSettings.GetArrayElementAtIndex(i);
            if (batchingArrayValue == null)
            {
                continue;
            }
            IEnumerator batchingEnumerator = batchingArrayValue.GetEnumerator();
            if (batchingEnumerator == null)
            {
                continue;
            }
            while (batchingEnumerator.MoveNext())
            {
                SerializedProperty property = (SerializedProperty)batchingEnumerator.Current;
                if (property != null && property.name == "m_StaticBatching")
                {
                    return property.boolValue;
                }
            }
        }
        return false;
    }

    void OnWizardUpdate()
    {
        helpString = "Aaro4130's OBJ Exporter " + versionString;
    }

    Vector3 RotateAroundPoint(Vector3 point, Vector3 pivot, Quaternion angle)
    {
        return angle * (point - pivot) + pivot;
    }
    Vector3 MultiplyVec3s(Vector3 v1, Vector3 v2)
    {
        return new Vector3(v1.x * v2.x, v1.y * v2.y, v1.z * v2.z);
    }

    bool canMergePoint(polygon cur, polygon next, Dictionary<int, int> paramMerge, List<Vector3> verticesList)
    {
        bool res = false;
        //是否有二个点共点，并且uv一样
        if (cur.poly.Count == 4)
        {
            return res;
        }
        var curA = -1;
        var curB = -1;
        var nextA = -1;
        var nextB = -1;
        int i = 0, j = 0;
        bool isSame = false;
        for (i = 0; i < cur.poly.Count; ++i)
        {
            curA = cur.getV(i);
            curB = cur.getV(i + 1);
            for (j = 0; j < next.poly.Count; ++j)
            {
                nextA = next.getV(j);
                nextB = next.getV(j + 1);
                if (curA == nextB && curB == nextA)
                {
                    if (cur.getVT(i) == next.getVT(j + 1) && cur.getVT(i + 1) == next.getVT(j))
                    {
                        /*
                     * 0    curIndex
                     * 1    nextIndex
                     * 2    deleteCount
                     * 3    delete1
                     * 4    delete2
                     */
                        paramMerge[0] = i;
                        paramMerge[1] = j;
                        isSame = true;
                        break;
                    }
                }
            }
        }
        //法向量是否平行
        if (isSame)
        {
            /**
             * 可能有方向不一样，不能合并，所以比较二个法向量
             */
            var curNor = getNormal(cur, verticesList);
            var nexNor = getNormal(next, verticesList);
            var test = curNor - nexNor;
            if (test.magnitude < 0.001)
            {
                res = true;
            }
        }
        if (cur.poly.Count == 3)
        {
            /**
             * 删除二个点
             */
            paramMerge[2] = 2;
        }
        return res;
    }

    Vector3 getNormal(polygon cur, List<Vector3> verticesList)
    {
        Vector3 v1 = verticesList[cur.getV(1)] - verticesList[cur.getV(0)];
        Vector3 v2 = verticesList[cur.getV(2)] - verticesList[cur.getV(1)];
        Vector3 res = Vector3.Cross(v1, v2);
        res.Normalize();
        return res;
    }

    void mergePoint(polygon cur, polygon next, Dictionary<int, int> paramMerge)
    {
        cur.poly.Insert(paramMerge[0] + 1, next.poly[(paramMerge[1] + 2) % next.poly.Count]);
    }

    List<polygon> mergeTriples(List<polygon> list, List<Vector3> verticesList)
    {
        List<polygon> res = new List<polygon>();
        int index = 0;
        while (list.Count > 0)
        {
            polygon current = list[0];
            list.RemoveAt(0);
            for (int j = 0; j < list.Count;)
            {
                polygon tmp = list[j];
                /*
                 * 0    curIndex
                 * 1    nextIndex
                 * 2    delete1
                 * 3    delete2
                 */
                Dictionary<int, int> paramMerge = new Dictionary<int, int>();

                if (canMergePoint(current, tmp, paramMerge, verticesList))
                {
                    mergePoint(current, tmp, paramMerge);
                    list.RemoveAt(j);
                    break;//暂时不处理四边形
                }
                else
                {
                    j++;
                }
            }
            res.Add(current);
        }
        return res;
    }

    void OnWizardCreate()
    {
        if (StaticBatchingEnabled() && Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Error", "Static batching is enabled. This will cause the export file to look like a mess, as well as be a large filesize. Disable this option, and restart the player, before continuing.", "OK");
            goto end;
        }
        if (autoMarkTexReadable)
        {
            int yes = EditorUtility.DisplayDialogComplex("Warning", "This will convert all textures to Advanced type with the read/write option set. This is not reversible and will permanently affect your project. Continue?", "Yes", "No", "Cancel");
            if (yes > 0)
            {
                goto end;
            }
        }
        string lastPath = EditorPrefs.GetString("a4_OBJExport_lastPath", "");
        string lastFileName = EditorPrefs.GetString("a4_OBJExport_lastFile", "unityexport.obj");
        string expFile = EditorUtility.SaveFilePanel("Export OBJ", lastPath, lastFileName, "obj");
        if (expFile.Length > 0)
        {
            var fi = new System.IO.FileInfo(expFile);
            EditorPrefs.SetString("a4_OBJExport_lastFile", fi.Name);
            EditorPrefs.SetString("a4_OBJExport_lastPath", fi.Directory.FullName);
            Export(expFile);
        }
    end:;
    }

    /**
     * 线性查找 
     */
    int findVector3(Vector3 v, List<Vector3> list)
    {
        int res = -1;
        for (int i = 0; i < list.Count; ++i)
        {
            Vector3 tmp = list[i];
            if (Mathf.Abs(tmp.x - v.x) < 0.001 && Mathf.Abs(tmp.y - v.y) < 0.001 && Mathf.Abs(tmp.z - v.z) < 0.001)
            {
                res = i;
                break;
            }
        }
        return res;
    }

    /**
     * 线性查找 
     */
    int findVector2(Vector2 v, List<Vector2> list)
    {
        int res = -1;
        for (int i = 0; i < list.Count; ++i)
        {
            Vector2 tmp = list[i];
            if (Mathf.Abs(tmp.x - v.x) < 0.001 && Mathf.Abs(tmp.y - v.y) < 0.001)
            {
                res = i;
                break;
            }
        }
        return res;
    }

    void Export(string exportPath)
    {
        //init stuff
        Dictionary<string, bool> materialCache = new Dictionary<string, bool>();
        var exportFileInfo = new System.IO.FileInfo(exportPath);
        lastExportFolder = exportFileInfo.Directory.FullName;
        string baseFileName = System.IO.Path.GetFileNameWithoutExtension(exportPath);
        EditorUtility.DisplayProgressBar("Exporting OBJ", "Please wait.. Starting export.", 0);

        //get list of required export things
        MeshFilter[] sceneMeshes;
        if (onlySelectedObjects)
        {
            List<MeshFilter> tempMFList = new List<MeshFilter>();
            foreach (GameObject g in Selection.gameObjects)
            {

                MeshFilter f = g.GetComponent<MeshFilter>();
                if (f != null)
                {
                    tempMFList.Add(f);
                }

            }
            sceneMeshes = tempMFList.ToArray();
        }
        else
        {
            sceneMeshes = FindObjectsOfType(typeof(MeshFilter)) as MeshFilter[];

        }

        if (Application.isPlaying)
        {
            foreach (MeshFilter mf in sceneMeshes)
            {
                MeshRenderer mr = mf.gameObject.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    if (mr.isPartOfStaticBatch)
                    {
                        EditorUtility.ClearProgressBar();
                        EditorUtility.DisplayDialog("Error", "Static batched object detected. Static batching is not compatible with this exporter. Please disable it before starting the player.", "OK");
                        return;
                    }
                }
            }
        }

        //work on export
        StringBuilder sb = new StringBuilder();
        StringBuilder sbMaterials = new StringBuilder();
        sb.AppendLine("# Export of " + Application.loadedLevelName);
        sb.AppendLine("# from Aaro4130 OBJ Exporter " + versionString);
        if (generateMaterials)
        {
            sb.AppendLine("mtllib " + baseFileName + ".mtl");
        }
        float maxExportProgress = (float)(sceneMeshes.Length + 1);
        int lastVertexIndex = 0;
        int lastVertexNormalsIndex = 0;
        int lastTextureCoordsIndex = 0;
        for (int i = 0; i < sceneMeshes.Length; i++)
        {
            string meshName = sceneMeshes[i].gameObject.name;
            float progress = (float)(i + 1) / maxExportProgress;
            EditorUtility.DisplayProgressBar("Exporting objects... (" + Mathf.Round(progress * 100) + "%)", "Exporting object " + meshName, progress);
            MeshFilter mf = sceneMeshes[i];
            MeshRenderer mr = sceneMeshes[i].gameObject.GetComponent<MeshRenderer>();

            if (mr != null && generateMaterials)
            {
                Material[] mats = mr.sharedMaterials;
                for (int j = 0; j < mats.Length; j++)
                {
                    Material m = mats[j];
                    if (!materialCache.ContainsKey(m.name))
                    {
                        materialCache[m.name] = true;
                        sbMaterials.Append(MaterialToString(m));
                        sbMaterials.AppendLine();
                    }
                }
            }

            //export the meshhh :3
            Mesh msh = mf.sharedMesh;
            int faceOrder = (int)Mathf.Clamp((mf.gameObject.transform.lossyScale.x * mf.gameObject.transform.lossyScale.z), -1, 1);

            //export vector data (FUN :D)!
            List<int> verticeIndexList = new List<int>();
            List<Vector3> verticeList = new List<Vector3>();
            foreach (Vector3 vx in msh.vertices)
            {
                Vector3 v = vx;
                int realIndex = findVector3(v, verticeList);
                if (realIndex == -1)
                {
                    verticeList.Add(v);
                    verticeIndexList.Add(verticeList.Count - 1);
                }
                else
                {
                    verticeIndexList.Add(realIndex);
                    continue;
                }

                if (applyScale)
                {
                    v = MultiplyVec3s(v, mf.gameObject.transform.lossyScale);
                }

                if (applyRotation)
                {

                    v = RotateAroundPoint(v, Vector3.zero, mf.gameObject.transform.rotation);
                }

                if (applyPosition)
                {
                    v += mf.gameObject.transform.position;
                }
                v.x *= -1;
                sb.AppendLine("v " + v.x + " " + v.y + " " + v.z);
            }
            List<int> normalIndexList = new List<int>();
            List<Vector3> normalList = new List<Vector3>();
            foreach (Vector3 vx in msh.normals)
            {
                Vector3 v = vx;
                int realIndex = findVector3(v, normalList);
                if (realIndex == -1)
                {
                    normalList.Add(v);
                    normalIndexList.Add(normalList.Count - 1);
                }
                else
                {
                    normalIndexList.Add(realIndex);
                    continue;
                }
                if (applyScale)
                {
                    v = MultiplyVec3s(v, mf.gameObject.transform.lossyScale.normalized);
                }
                if (applyRotation)
                {
                    v = RotateAroundPoint(v, Vector3.zero, mf.gameObject.transform.rotation);
                }
                v.x *= -1;
                sb.AppendLine("vn " + v.x + " " + v.y + " " + v.z);

            }
            List<int> uvIndexList = new List<int>();
            List<Vector2> uvList = new List<Vector2>();
            foreach (Vector2 v in msh.uv)
            {
                Vector2 vv = v;
                int realIndex = findVector2(v, uvList);
                if (realIndex == -1)
                {
                    uvList.Add(v);
                    uvIndexList.Add(uvList.Count - 1);
                }
                else
                {
                    uvIndexList.Add(realIndex);
                    continue;
                }
                sb.AppendLine("vt " + v.x + " " + v.y);
            }

            if (splitObjects)
            {
                string exportName = meshName;
                if (objNameAddIdNum)
                {
                    exportName += "_" + i;
                }
                sb.AppendLine("g " + exportName);
            }

            for (int j = 0; j < msh.subMeshCount; j++)
            {
                if (generateMaterials)
                {
                    if (mr != null && j < mr.sharedMaterials.Length)
                    {
                        string matName = mr.sharedMaterials[j].name;
                        sb.AppendLine("usemtl " + matName);
                    }
                    else
                    {
                        sb.AppendLine("usemtl " + meshName + "_sm" + j);
                    }
                }

                int[] tris = msh.GetTriangles(j);
                List<polygon> triplesList = new List<polygon>();
                for (int t = 0; t < tris.Length; t += 3)
                {
                    polygon tri = new polygon();
                    tri.poly.Add(new triples(verticeIndexList[tris[t]], normalIndexList[tris[t]], uvIndexList[tris[t]]));
                    tri.poly.Add(new triples(verticeIndexList[tris[t + 1]], normalIndexList[tris[t + 1]], uvIndexList[tris[t + 1]]));
                    tri.poly.Add(new triples(verticeIndexList[tris[t + 2]], normalIndexList[tris[t + 2]], uvIndexList[tris[t + 2]]));
                    triplesList.Add(tri);
                }
                triplesList = mergeTriples(triplesList, verticeList);
                for (int t = 0; t < triplesList.Count; ++t)
                {
                    if (triplesList[t].poly.Count == 3)
                    {
                        if (faceOrder < 0)
                        {
                            sb.AppendLine("f " + ConstructOBJString2(triplesList[t].poly[2].v + 1 + lastVertexIndex, triplesList[t].poly[2].vn + 1 + lastVertexNormalsIndex, triplesList[t].poly[2].vt + 1 + lastTextureCoordsIndex) + " " + ConstructOBJString2(triplesList[t].poly[1].v + 1 + lastVertexIndex, triplesList[t].poly[1].vn + 1 + lastVertexNormalsIndex, triplesList[t].poly[1].vt + 1 + lastTextureCoordsIndex) + " " + ConstructOBJString2(triplesList[t].poly[0].v + 1 + lastVertexIndex, triplesList[t].poly[0].vn + 1 + lastVertexNormalsIndex, triplesList[t].poly[0].vt + 1 + lastTextureCoordsIndex));
                        }
                        else
                        {
                            sb.AppendLine("f " + ConstructOBJString2(triplesList[t].poly[0].v + 1 + lastVertexIndex, triplesList[t].poly[0].vn + 1 + lastVertexNormalsIndex, triplesList[t].poly[0].vt + 1 + lastTextureCoordsIndex) + " " + ConstructOBJString2(triplesList[t].poly[1].v + 1 + lastVertexIndex, triplesList[t].poly[1].vn + 1 + lastVertexNormalsIndex, triplesList[t].poly[1].vt + 1 + lastTextureCoordsIndex) + " " + ConstructOBJString2(triplesList[t].poly[2].v + 1 + lastVertexIndex, triplesList[t].poly[2].vn + 1 + lastVertexNormalsIndex, triplesList[t].poly[2].vt + 1 + lastTextureCoordsIndex));
                        }
                    }
                    else
                    {
                        if (faceOrder < 0)
                        {
                            sb.AppendLine("f " + ConstructOBJString2(triplesList[t].poly[3].v + 1 + lastVertexIndex, triplesList[t].poly[3].vn + 1 + lastVertexNormalsIndex, triplesList[t].poly[3].vt + 1 + lastTextureCoordsIndex) + " " + ConstructOBJString2(triplesList[t].poly[2].v + 1 + lastVertexIndex, triplesList[t].poly[2].vn + 1 + lastVertexNormalsIndex, triplesList[t].poly[2].vt + 1 + lastTextureCoordsIndex) + " " + ConstructOBJString2(triplesList[t].poly[1].v + 1 + lastVertexIndex, triplesList[t].poly[1].vn + 1 + lastVertexNormalsIndex, triplesList[t].poly[1].vt + 1 + lastTextureCoordsIndex) + " " + ConstructOBJString2(triplesList[t].poly[0].v + 1 + lastVertexIndex, triplesList[t].poly[0].vn + 1 + lastVertexNormalsIndex, triplesList[t].poly[0].vt + 1 + lastTextureCoordsIndex));
                        }
                        else
                        {
                            sb.AppendLine("f " + ConstructOBJString2(triplesList[t].poly[0].v + 1 + lastVertexIndex, triplesList[t].poly[0].vn + 1 + lastVertexNormalsIndex, triplesList[t].poly[0].vt + 1 + lastTextureCoordsIndex) + " " + ConstructOBJString2(triplesList[t].poly[1].v + 1 + lastVertexIndex, triplesList[t].poly[1].vn + 1 + lastVertexNormalsIndex, triplesList[t].poly[1].vt + 1 + lastTextureCoordsIndex) + " " + ConstructOBJString2(triplesList[t].poly[2].v + 1 + lastVertexIndex, triplesList[t].poly[2].vn + 1 + lastVertexNormalsIndex, triplesList[t].poly[2].vt + 1 + lastTextureCoordsIndex) + " " + ConstructOBJString2(triplesList[t].poly[3].v + 1 + lastVertexIndex, triplesList[t].poly[3].vn + 1 + lastVertexNormalsIndex, triplesList[t].poly[3].vt + 1 + lastTextureCoordsIndex));
                        }
                    }

                }
            }
            lastVertexIndex += verticeList.Count;
            lastVertexNormalsIndex += normalList.Count;
            lastTextureCoordsIndex += uvList.Count;
        }

        //write to disk
        System.IO.File.WriteAllText(exportPath, sb.ToString());
        if (generateMaterials)
        {
            System.IO.File.WriteAllText(exportFileInfo.Directory.FullName + "\\" + baseFileName + ".mtl", sbMaterials.ToString());
        }

        //export complete, close progress dialog
        EditorUtility.ClearProgressBar();
        EditorUtility.DisplayDialog("Succeed!", "Scene Objects has been exported to: " + Path.GetFullPath(exportPath), "OK");
    }

    string TryExportTexture(string propertyName, Material m)
    {
        if (m.HasProperty(propertyName))
        {
            Texture t = m.GetTexture(propertyName);
            if (t != null)
            {
                return ExportTexture((Texture2D)t);
            }
        }
        return "false";
    }
    string ExportTexture(Texture2D t)
    {
        try
        {
            if (autoMarkTexReadable)
            {
                string assetPath = AssetDatabase.GetAssetPath(t);
                var tImporter = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (tImporter != null)
                {
                    tImporter.textureType = TextureImporterType.Default;

                    if (!tImporter.isReadable)
                    {
                        tImporter.isReadable = true;

                        AssetDatabase.ImportAsset(assetPath);
                        AssetDatabase.Refresh();
                    }
                }
            }
            string exportName = lastExportFolder + "\\" + t.name + ".png";
            Texture2D exTexture = new Texture2D(t.width, t.height, TextureFormat.ARGB32, false);
            exTexture.SetPixels(t.GetPixels());
            System.IO.File.WriteAllBytes(exportName, exTexture.EncodeToPNG());
            return exportName;
        }
        catch (System.Exception ex)
        {
            Debug.Log("Could not export texture : " + t.name + ". is it readable?");
            return "null";
        }

    }

    private string ConstructOBJString(int index)
    {
        string idxString = index.ToString();
        return idxString + "/" + idxString + "/" + idxString;
    }

    private string ConstructOBJString2(int vertexIndex, int indexVN, int indexUV)
    {
        return vertexIndex.ToString() + "/" + indexUV.ToString() + "/" + indexVN.ToString();
    }

    string MaterialToString(Material m)
    {
        StringBuilder sb = new StringBuilder();

        sb.AppendLine("newmtl " + m.name);


        //add properties
        if (m.HasProperty("_Color"))
        {
            sb.AppendLine("Kd " + m.color.r.ToString() + " " + m.color.g.ToString() + " " + m.color.b.ToString());
            if (m.color.a < 1.0f)
            {
                //use both implementations of OBJ transparency
                sb.AppendLine("Tr " + (1f - m.color.a).ToString());
                sb.AppendLine("d " + m.color.a.ToString());
            }
        }
        if (m.HasProperty("_SpecColor"))
        {
            Color sc = m.GetColor("_SpecColor");
            sb.AppendLine("Ks " + sc.r.ToString() + " " + sc.g.ToString() + " " + sc.b.ToString());
        }
        if (exportTextures)
        {
            //diffuse
            string exResult = TryExportTexture("_MainTex", m);
            if (exResult != "false")
            {
                sb.AppendLine("map_Kd " + exResult);
            }
            //spec map
            exResult = TryExportTexture("_SpecMap", m);
            if (exResult != "false")
            {
                sb.AppendLine("map_Ks " + exResult);
            }
            //bump map
            exResult = TryExportTexture("_BumpMap", m);
            if (exResult != "false")
            {
                sb.AppendLine("map_Bump " + exResult);
            }

        }
        sb.AppendLine("illum 2");
        return sb.ToString();
    }
    [MenuItem("File/Export/Wavefront OBJ")]
    static void CreateWizard()
    {
        ScriptableWizard.DisplayWizard("Export OBJ", typeof(OBJExporter), "Export");
    }
}
#endif