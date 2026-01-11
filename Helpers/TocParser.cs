using System.Text.Json;
using LawAfrica.API.Models;

namespace LawAfrica.API.Helpers
{
    public static class TocParser
    {
        public static List<TocNode> ParseOrEmpty(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new();

            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var nodes = JsonSerializer.Deserialize<List<TocNode>>(json, options) ?? new();
                Normalize(nodes);
                return nodes;
            }
            catch
            {
                // Never break frontend
                return new();
            }
        }

        private static void Normalize(List<TocNode> nodes)
        {
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                var node = nodes[i];

                node.Title = (node.Title ?? "").Trim();
                node.Children ??= new();

                if (string.IsNullOrWhiteSpace(node.Title) || node.Page < 1)
                {
                    nodes.RemoveAt(i);
                    continue;
                }

                Normalize(node.Children);
            }
        }
    }
}
