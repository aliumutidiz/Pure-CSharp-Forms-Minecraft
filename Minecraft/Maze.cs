/*
 * 
 * 
 * 
 * 
 * 
 * 
 * 
 * 
 * 
 *     This file is for testing purposes only.
 * 
 * 
 * 
 * 
 * 
 * 
 * 
 * 
 * 
 */



using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Newtonsoft.Json;

namespace Minecraft
{
    public static class Maze
    {
        public static void BuildMazeFromFile(Form1 form, string path = "./bin/savedmap.json", int fixedY = 1)
        {
            if (!File.Exists(path))
            {
                Console.WriteLine($"Maze file not found: {path}");
                return;
            }

            var json = File.ReadAllText(path);
            var mazeData = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
            int maxX = 0;
            int maxY = 0;
            foreach (var key in mazeData.Keys)
            {
                var coords = key.Split(',');
                int x = int.Parse(coords[0]);
                int y = int.Parse(coords[1]);

                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }


            int width = maxX + 1;
            int height = maxY + 1;
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                   
                    form.AddBlock(x,fixedY-1,y);
                }
               
            }


            for (int i = fixedY; i <= fixedY+1; i++)
            {
                foreach (var entry in mazeData)
                {
                    string[] parts = entry.Key.Split(',');
                    if (parts.Length != 2) continue;

                    if (int.TryParse(parts[0].Trim(), out int x) &&
                        int.TryParse(parts[1].Trim(), out int z))
                    {
                        Color color = Color.FromName(entry.Value.Trim());
                        form.AddBlock(x, i, z);
                        // Debug output to console
                        // Console.WriteLine($"Block added at ({x}, {i}, {z}) with color {color.Name}.");
                    }
                }
            }

          
        }
    }
}
