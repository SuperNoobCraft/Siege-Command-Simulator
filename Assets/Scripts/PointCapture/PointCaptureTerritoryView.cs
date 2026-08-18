using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds a filled control overlay and border lines from owned villages plus adjacent corridors.
/// </summary>
[DefaultExecutionOrder(50)]
[DisallowMultipleComponent]
[ExecuteAlways]
public class PointCaptureTerritoryView : MonoBehaviour
{
    [SerializeField] private PointCaptureBoard board;
    [SerializeField, Min(0.5f)] private float cellSize = 1.5f;
    [SerializeField] private float fillHeight = 0.06f;
    [SerializeField] private float mapPadding = 18f;
    [SerializeField] private Color redFill = new Color(0.86f, 0.16f, 0.12f, 0.32f);
    [SerializeField] private Color yellowFill = new Color(0.95f, 0.78f, 0.12f, 0.32f);
    [SerializeField] private Color redBorder = new Color(0.95f, 0.28f, 0.18f, 0.95f);
    [SerializeField] private Color yellowBorder = new Color(1f, 0.92f, 0.25f, 0.95f);

    private Mesh redMesh;
    private Mesh yellowMesh;
    private MeshFilter redFilter;
    private MeshFilter yellowFilter;
    private MeshRenderer redRenderer;
    private MeshRenderer yellowRenderer;
    private readonly List<Vector3> redBorderSegments = new List<Vector3>();
    private readonly List<Vector3> yellowBorderSegments = new List<Vector3>();
    private Material lineMaterial;

    public void Configure(PointCaptureBoard captureBoard, Material redMaterial, Material yellowMaterial)
    {
        if (board != null)
        {
            board.TerritoryChanged -= Rebuild;
        }

        board = captureBoard;
        if (board != null)
        {
            board.TerritoryChanged += Rebuild;
        }

        EnsureFillObjects(redMaterial, yellowMaterial);
        Rebuild();
    }

    private void OnEnable()
    {
        if (board == null)
        {
            board = PointCaptureBoard.Instance;
        }

        if (board != null)
        {
            board.TerritoryChanged -= Rebuild;
            board.TerritoryChanged += Rebuild;
        }
    }

    private void Start()
    {
        if (board == null)
        {
            board = PointCaptureBoard.Instance;
        }

        Rebuild();
    }

    private void OnDisable()
    {
        if (board != null)
        {
            board.TerritoryChanged -= Rebuild;
        }
    }

    private void OnDestroy()
    {
        if (redMesh != null)
        {
            DestroyMesh(redMesh);
        }

        if (yellowMesh != null)
        {
            DestroyMesh(yellowMesh);
        }
    }

    public void Rebuild()
    {
        if (board == null)
        {
            return;
        }

        EnsureFillObjects(null, null);
        BuildOwnerMesh(CaptureOwner.Red, ref redMesh, redFilter);
        BuildOwnerMesh(CaptureOwner.Yellow, ref yellowMesh, yellowFilter);
        BuildOwnerBorderSegments(CaptureOwner.Red, redBorderSegments);
        BuildOwnerBorderSegments(CaptureOwner.Yellow, yellowBorderSegments);
    }

    private void EnsureFillObjects(Material redMaterial, Material yellowMaterial)
    {
        if (redFilter == null)
        {
            CreateFillChild("RedTerritory", ref redFilter, ref redRenderer, redMaterial, redFill);
        }
        else if (redMaterial != null)
        {
            redRenderer.sharedMaterial = redMaterial;
        }

        if (yellowFilter == null)
        {
            CreateFillChild("YellowTerritory", ref yellowFilter, ref yellowRenderer, yellowMaterial, yellowFill);
        }
        else if (yellowMaterial != null)
        {
            yellowRenderer.sharedMaterial = yellowMaterial;
        }
    }

    private void CreateFillChild(
        string childName,
        ref MeshFilter filter,
        ref MeshRenderer meshRenderer,
        Material material,
        Color fallbackColor)
    {
        Transform existing = transform.Find(childName);
        GameObject child = existing != null ? existing.gameObject : new GameObject(childName);
        child.transform.SetParent(transform, false);
        filter = child.GetComponent<MeshFilter>();
        if (filter == null)
        {
            filter = child.AddComponent<MeshFilter>();
        }

        meshRenderer = child.GetComponent<MeshRenderer>();
        if (meshRenderer == null)
        {
            meshRenderer = child.AddComponent<MeshRenderer>();
        }

        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        if (material != null)
        {
            meshRenderer.sharedMaterial = material;
        }
        else if (meshRenderer.sharedMaterial == null)
        {
            meshRenderer.sharedMaterial = CreateRuntimeFillMaterial(fallbackColor);
        }
    }

    private void BuildOwnerMesh(CaptureOwner owner, ref Mesh mesh, MeshFilter filter)
    {
        if (filter == null || board.Villages == null || board.Villages.Length == 0)
        {
            return;
        }

        Bounds bounds = CalculateMapBounds();
        int cellsX = Mathf.Max(2, Mathf.CeilToInt(bounds.size.x / cellSize));
        int cellsZ = Mathf.Max(2, Mathf.CeilToInt(bounds.size.z / cellSize));
        List<Vector3> vertices = new List<Vector3>(cellsX * cellsZ * 4);
        List<int> triangles = new List<int>(cellsX * cellsZ * 6);

        for (int z = 0; z < cellsZ; z++)
        {
            for (int x = 0; x < cellsX; x++)
            {
                float minX = bounds.min.x + x * cellSize;
                float minZ = bounds.min.z + z * cellSize;
                Vector3 center = new Vector3(minX + cellSize * 0.5f, fillHeight, minZ + cellSize * 0.5f);
                if (board.GetControllingOwner(center) != owner)
                {
                    continue;
                }

                int vertexIndex = vertices.Count;
                Vector3 v0 = filter.transform.InverseTransformPoint(new Vector3(minX, fillHeight, minZ));
                Vector3 v1 = filter.transform.InverseTransformPoint(new Vector3(minX + cellSize, fillHeight, minZ));
                Vector3 v2 = filter.transform.InverseTransformPoint(new Vector3(minX + cellSize, fillHeight, minZ + cellSize));
                Vector3 v3 = filter.transform.InverseTransformPoint(new Vector3(minX, fillHeight, minZ + cellSize));
                vertices.Add(v0);
                vertices.Add(v1);
                vertices.Add(v2);
                vertices.Add(v3);
                triangles.Add(vertexIndex);
                triangles.Add(vertexIndex + 2);
                triangles.Add(vertexIndex + 1);
                triangles.Add(vertexIndex);
                triangles.Add(vertexIndex + 3);
                triangles.Add(vertexIndex + 2);
            }
        }

        if (mesh == null)
        {
            mesh = new Mesh { name = owner + "TerritoryMesh" };
            mesh.MarkDynamic();
        }

        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        filter.sharedMesh = mesh;
    }

    private void BuildOwnerBorderSegments(CaptureOwner owner, List<Vector3> borderSegments)
    {
        borderSegments.Clear();
        if (board.Villages == null || board.Villages.Length == 0)
        {
            return;
        }

        Bounds bounds = CalculateMapBounds();
        int cellsX = Mathf.Max(2, Mathf.CeilToInt(bounds.size.x / cellSize));
        int cellsZ = Mathf.Max(2, Mathf.CeilToInt(bounds.size.z / cellSize));

        for (int z = 0; z < cellsZ; z++)
        {
            for (int x = 0; x < cellsX; x++)
            {
                float minX = bounds.min.x + x * cellSize;
                float minZ = bounds.min.z + z * cellSize;
                Vector3 center = new Vector3(minX + cellSize * 0.5f, fillHeight, minZ + cellSize * 0.5f);
                if (board.GetControllingOwner(center) != owner)
                {
                    continue;
                }

                AddBorderIfNeeded(owner, minX, minZ, 0f, -cellSize, borderSegments, fillHeight);
                AddBorderIfNeeded(owner, minX, minZ, cellSize, 0f, borderSegments, fillHeight);
                AddBorderIfNeeded(owner, minX, minZ, 0f, cellSize, borderSegments, fillHeight);
                AddBorderIfNeeded(owner, minX, minZ, -cellSize, 0f, borderSegments, fillHeight);
            }
        }
    }

    private void AddBorderIfNeeded(
        CaptureOwner owner,
        float minX,
        float minZ,
        float neighborOffsetX,
        float neighborOffsetZ,
        List<Vector3> borderSegments,
        float height)
    {
        Vector3 neighbor = new Vector3(
            minX + cellSize * 0.5f + neighborOffsetX,
            height,
            minZ + cellSize * 0.5f + neighborOffsetZ);
        if (board.GetControllingOwner(neighbor) == owner)
        {
            return;
        }

        Vector3 a;
        Vector3 b;
        if (Mathf.Abs(neighborOffsetX) > Mathf.Abs(neighborOffsetZ))
        {
            float edgeX = neighborOffsetX > 0f ? minX + cellSize : minX;
            a = new Vector3(edgeX, height + 0.02f, minZ);
            b = new Vector3(edgeX, height + 0.02f, minZ + cellSize);
        }
        else
        {
            float edgeZ = neighborOffsetZ > 0f ? minZ + cellSize : minZ;
            a = new Vector3(minX, height + 0.02f, edgeZ);
            b = new Vector3(minX + cellSize, height + 0.02f, edgeZ);
        }

        borderSegments.Add(a);
        borderSegments.Add(b);
    }

    private Bounds CalculateMapBounds()
    {
        PointCaptureVillage[] villages = board.Villages;
        Vector3 min = villages[0].Position;
        Vector3 max = min;
        for (int i = 1; i < villages.Length; i++)
        {
            if (villages[i] == null)
            {
                continue;
            }

            Vector3 p = villages[i].Position;
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        min.x -= mapPadding;
        min.z -= mapPadding;
        max.x += mapPadding;
        max.z += mapPadding;
        min.y = 0f;
        max.y = 1f;
        Bounds bounds = new Bounds();
        bounds.SetMinMax(min, max);
        return bounds;
    }

    private void OnRenderObject()
    {
        if (!EnsureLineMaterial())
        {
            return;
        }

        DrawSegments(redBorderSegments, redBorder);
        DrawSegments(yellowBorderSegments, yellowBorder);
    }

    private void DrawSegments(List<Vector3> segments, Color color)
    {
        if (segments == null || segments.Count < 2)
        {
            return;
        }

        lineMaterial.SetPass(0);
        GL.PushMatrix();
        GL.MultMatrix(Matrix4x4.identity);
        GL.Begin(GL.LINES);
        GL.Color(color);
        for (int i = 0; i + 1 < segments.Count; i += 2)
        {
            GL.Vertex(segments[i]);
            GL.Vertex(segments[i + 1]);
        }

        GL.End();
        GL.PopMatrix();
    }

    private bool EnsureLineMaterial()
    {
        if (lineMaterial != null)
        {
            return true;
        }

        Shader shader = Shader.Find("Hidden/Internal-Colored");
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        if (shader == null)
        {
            return false;
        }

        lineMaterial = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        lineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        lineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        lineMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        lineMaterial.SetInt("_ZWrite", 0);
        return true;
    }

    private static Material CreateRuntimeFillMaterial(Color color)
    {
        Shader shader = Shader.Find("Standard");
        Material material = shader != null ? new Material(shader) : new Material(Shader.Find("Sprites/Default"));
        material.color = color;
        if (material.HasProperty("_Mode"))
        {
            material.SetFloat("_Mode", 3f);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = 3000;
        }

        material.hideFlags = HideFlags.HideAndDontSave;
        return material;
    }

    private static void DestroyMesh(Mesh mesh)
    {
        if (Application.isPlaying)
        {
            Destroy(mesh);
        }
        else
        {
            DestroyImmediate(mesh);
        }
    }
}
