using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Minecraft
{
    public class LoadMap
    {
        public static void LoadMapFromFile(Form1 form, string path = "./bin/savedmap.json", int fixedY = 1)
        {
            if (!File.Exists(path))
            {
                Console.WriteLine($"Maze file not found: {path}");
                return;
            }

            var json = File.ReadAllText(path);
            var blockData = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);

            foreach (var entry in blockData)
            {
                string[] parts = entry.Key.Split(',');
                if (parts.Length != 3) continue;
                if (int.TryParse(parts[0].Trim(), out int x) &&
                    int.TryParse(parts[1].Trim(), out int y) &&
                    int.TryParse(parts[2].Trim(), out int z))
                {
                    Color color = Color.FromName(entry.Value.Trim());
                    form.AddBlock(x, y, z);
                }
            }
        }
    }
}