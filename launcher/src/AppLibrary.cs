using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

// The Apps menu's catalog (res/quiet-home/apps.json; docs/app-library.md): installed apps with local artwork.
public sealed class LibraryApp
{
    public string Name { get; set; }
    public string Id { get; set; }
    public string Asset { get; set; }  // relative to the resource directory
    public string Fit { get; set; } = "cover";  // cover | contain | contain-full
    public string Background { get; set; } = "#FFFFFF";
    public int Width { get; set; }
    public int Height { get; set; }
    public float[] Crop { get; set; }  // normalized [x, y, width, height] of the asset
}

public static class AppLibrary
{
    public static List<LibraryApp> Load(string path)
    {
        var apps = JsonSerializer.Deserialize<List<LibraryApp>>(File.ReadAllText(path));
        if (apps == null || apps.Count == 0) throw new InvalidDataException("App library is empty.");
        var ids = new HashSet<string>();
        foreach (var app in apps) {
            if (string.IsNullOrWhiteSpace(app.Name) || string.IsNullOrWhiteSpace(app.Id) || !ids.Add(app.Id) || string.IsNullOrWhiteSpace(app.Asset)
                || Path.IsPathRooted(app.Asset) || app.Asset.Contains("..") || app.Width <= 0 || app.Height <= 0)
                throw new InvalidDataException("Invalid app library entry.");
            var c = app.Crop;
            if (c != null && (c.Length != 4 || !Array.TrueForAll(c, float.IsFinite) || c[0] < 0 || c[1] < 0 || c[2] <= 0 || c[3] <= 0 || c[0] + c[2] > 1 || c[1] + c[3] > 1))
                throw new InvalidDataException("Invalid artwork crop.");
        }
        return apps;
    }
}
