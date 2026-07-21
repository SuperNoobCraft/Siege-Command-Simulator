using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Votanic.vNet.Networking;
using Votanic.vXR.vGear.Networking;

/// <summary>
/// Reads a network config from the export root (next to the .exe) and applies it to
/// <see cref="vGear_Networking"/> before it auto-connects.
///
/// Supported files (first match wins):
///   network-configc.json / network-configh.json
///   network-configc.txt / network-configh.txt
///   network-configc / network-configh
///   network-config.json / network-config.txt
///
/// Example JSON:
/// {
///   "role": "client",
///   "username": "CaveB",
///   "host-ip": "192.168.0.242",
///   "port": 7777
/// }
///
/// Example TXT (key: value):
/// role: client
/// username: CaveB
/// host-ip: 192.168.0.242
/// port: 7777
///
/// Mapping:
///   role      → networking.type       (host / client)
///   username  → networking.networkName
///   host-ip   → networking.address
///   port      → networking.port (optional)
/// </summary>
[DefaultExecutionOrder(-500)]
[DisallowMultipleComponent]
[RequireComponent(typeof(vGear_Networking))]
public class VGearNetworkConfigLoader : MonoBehaviour
{
    public const string JsonFileName = "network-config.json";
    public const string TxtFileName = "network-config.txt";
    public const string ClientJsonFileName = "network-configc.json";
    public const string HostJsonFileName = "network-configh.json";
    public const string ClientTxtFileName = "network-configc.txt";
    public const string HostTxtFileName = "network-configh.txt";
    public const string ClientFileName = "network-configc";
    public const string HostFileName = "network-configh";

    private static readonly string[] ConfigSearchOrder =
    {
        ClientJsonFileName,
        HostJsonFileName,
        ClientTxtFileName,
        HostTxtFileName,
        ClientFileName,
        HostFileName,
        JsonFileName,
        TxtFileName
    };

    [SerializeField] private vGear_Networking networking;
    [Tooltip("In player builds only: create network-config.json next to the exe on first launch if missing.")]
    [SerializeField] private bool writeExampleIfMissingInPlayer = true;
    [SerializeField] private bool logConfigLoad = true;

    [Header("Editor fallback")]
    [Tooltip("Also look in the Unity project root while playing in the Editor.")]
    [SerializeField] private bool alsoSearchProjectRootInEditor = true;

    private void Reset()
    {
        networking = GetComponent<vGear_Networking>();
    }

    private void Awake()
    {
        if (networking == null)
        {
            networking = GetComponent<vGear_Networking>();
        }

        if (networking == null)
        {
            Debug.LogError("VGearNetworkConfigLoader: no vGear_Networking on this object.", this);
            return;
        }

        ApplyConfigFromDisk();
    }

    [ContextMenu("Reload Network Config Now")]
    public void ApplyConfigFromDisk()
    {
        if (networking == null)
        {
            networking = GetComponent<vGear_Networking>();
        }

        if (networking == null)
        {
            return;
        }

        string configPath = ResolveConfigPath(out bool createdExample);
        if (string.IsNullOrEmpty(configPath))
        {
            if (logConfigLoad)
            {
                Debug.LogWarning(
                    "VGearNetworkConfigLoader: no network config found next to the export "
                    + "(network-configc/h, network-config.json, or network-config.txt). "
                    + "Using inspector values on vGear_Networking.",
                    this);
            }

            return;
        }

        if (!TryReadConfig(configPath, out NetworkConfigFile config, out string error))
        {
            Debug.LogError(
                "VGearNetworkConfigLoader: failed to read '" + configPath + "': " + error,
                this);
            return;
        }

        ApplyConfig(config);

        if (logConfigLoad)
        {
            Debug.Log(
                "VGearNetworkConfigLoader: applied '" + configPath + "'"
                + (createdExample ? " (wrote example file)" : string.Empty)
                + " → type=" + networking.type
                + ", networkName='" + networking.networkName + "'"
                + ", address='" + networking.address + "'"
                + ", port=" + networking.port,
                this);
        }
    }

    private void ApplyConfig(NetworkConfigFile config)
    {
        if (!string.IsNullOrWhiteSpace(config.role)
            && TryParseRole(config.role, out UserType userType))
        {
            networking.type = userType;
        }

        if (!string.IsNullOrWhiteSpace(config.username))
        {
            networking.networkName = config.username.Trim();
        }

        if (!string.IsNullOrWhiteSpace(config.hostIp))
        {
            networking.address = config.hostIp.Trim();
        }

        if (config.port > 0)
        {
            networking.port = config.port;
            if (networking.uNetManager != null)
            {
                networking.uNetManager.networkPort = config.port;
            }
        }
    }

    private string ResolveConfigPath(out bool createdExample)
    {
        createdExample = false;

        List<string> searchRoots = new List<string>();
        string exportRoot = GetExportRootDirectory();
        if (!string.IsNullOrEmpty(exportRoot))
        {
            searchRoots.Add(exportRoot);
        }

#if UNITY_EDITOR
        if (alsoSearchProjectRootInEditor)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (!string.IsNullOrEmpty(projectRoot) && !searchRoots.Contains(projectRoot))
            {
                searchRoots.Add(projectRoot);
            }
        }
#endif

        for (int i = 0; i < searchRoots.Count; i++)
        {
            string root = searchRoots[i];
            for (int f = 0; f < ConfigSearchOrder.Length; f++)
            {
                string candidate = Path.Combine(root, ConfigSearchOrder[f]);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        if (writeExampleIfMissingInPlayer
            && !Application.isEditor
            && !string.IsNullOrEmpty(exportRoot))
        {
            string examplePath = Path.Combine(exportRoot, JsonFileName);
            try
            {
                File.WriteAllText(examplePath, BuildExampleJson(), Encoding.UTF8);
                createdExample = true;
                return examplePath;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "VGearNetworkConfigLoader: could not write example config to '"
                    + examplePath + "': " + exception.Message,
                    this);
            }
        }

        return null;
    }

    private static string GetExportRootDirectory()
    {
        // Standalone: Application.dataPath is <Game>_Data → parent folder holds the .exe.
        // Editor: dataPath is <Project>/Assets → parent is project root.
        try
        {
            DirectoryInfo dataDir = new DirectoryInfo(Application.dataPath);
            return dataDir.Parent != null ? dataDir.Parent.FullName : Application.dataPath;
        }
        catch
        {
            return Application.dataPath;
        }
    }

    private static bool TryReadConfig(string path, out NetworkConfigFile config, out string error)
    {
        config = null;
        error = null;

        string text;
        try
        {
            text = File.ReadAllText(path, Encoding.UTF8);
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "file is empty";
            return false;
        }

        string extension = Path.GetExtension(path);
        if (string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase)
            || text.TrimStart().StartsWith("{", StringComparison.Ordinal))
        {
            return TryParseJson(text, out config, out error);
        }

        return TryParseKeyValueText(text, out config, out error);
    }

    private static bool TryParseJson(string text, out NetworkConfigFile config, out string error)
    {
        config = null;
        error = null;

        // JsonUtility cannot bind hyphenated keys — normalize common aliases first.
        string normalized = text
            .Replace("\"host-ip\"", "\"hostIp\"")
            .Replace("\"host_ip\"", "\"hostIp\"")
            .Replace("\"Host-Ip\"", "\"hostIp\"")
            .Replace("\"HostIp\"", "\"hostIp\"")
            .Replace("\"user-name\"", "\"username\"")
            .Replace("\"user_name\"", "\"username\"")
            .Replace("\"network-name\"", "\"username\"")
            .Replace("\"networkName\"", "\"username\"");

        try
        {
            config = JsonUtility.FromJson<NetworkConfigFile>(normalized);
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }

        if (config == null)
        {
            error = "JsonUtility returned null";
            return false;
        }

        return true;
    }

    private static bool TryParseKeyValueText(string text, out NetworkConfigFile config, out string error)
    {
        config = new NetworkConfigFile();
        error = null;

        string[] lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            int separator = line.IndexOf(':');
            if (separator < 0)
            {
                separator = line.IndexOf('=');
            }

            if (separator <= 0)
            {
                continue;
            }

            string key = NormalizeKey(line.Substring(0, separator));
            string value = line.Substring(separator + 1).Trim().Trim('"');

            switch (key)
            {
                case "role":
                case "type":
                    config.role = value;
                    break;
                case "username":
                case "user":
                case "networkname":
                case "name":
                    config.username = value;
                    break;
                case "hostip":
                case "host":
                case "ip":
                case "address":
                    config.hostIp = value;
                    break;
                case "port":
                    if (int.TryParse(value, out int port))
                    {
                        config.port = port;
                    }

                    break;
            }
        }

        return true;
    }

    private static string NormalizeKey(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        StringBuilder builder = new StringBuilder(key.Length);
        for (int i = 0; i < key.Length; i++)
        {
            char c = key[i];
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }

    private static bool TryParseRole(string role, out UserType userType)
    {
        userType = UserType.Client;
        if (string.IsNullOrWhiteSpace(role))
        {
            return false;
        }

        string normalized = role.Trim();
        if (normalized.Equals("host", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("server", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("0", StringComparison.OrdinalIgnoreCase))
        {
            userType = UserType.Host;
            return true;
        }

        if (normalized.Equals("client", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("1", StringComparison.OrdinalIgnoreCase))
        {
            userType = UserType.Client;
            return true;
        }

        return Enum.TryParse(normalized, ignoreCase: true, out userType);
    }

    private string BuildExampleJson()
    {
        string role = networking != null && networking.type == UserType.Host ? "host" : "client";
        string username = networking != null && !string.IsNullOrEmpty(networking.networkName)
            ? networking.networkName
            : "CaveUser";
        string hostIp = networking != null && !string.IsNullOrEmpty(networking.address)
            ? networking.address
            : "127.0.0.1";
        int port = networking != null && networking.port > 0 ? networking.port : 7777;

        return "{\n"
            + "  \"role\": \"" + role + "\",\n"
            + "  \"username\": \"" + username + "\",\n"
            + "  \"host-ip\": \"" + hostIp + "\",\n"
            + "  \"port\": " + port + "\n"
            + "}\n";
    }

    [Serializable]
    private class NetworkConfigFile
    {
        public string role;
        public string username;
        public string hostIp;
        public int port;
    }
}
