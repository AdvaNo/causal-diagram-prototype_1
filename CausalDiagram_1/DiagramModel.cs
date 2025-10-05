using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace CausalDiagram_1
{
    public class Node
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Title { get; set; } = "Узел";
        public string Description { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
        public float Weight { get; set; } = 0f;

        // FMEA поля
        public int Severity { get; set; } = 1;
        public int Occurrence { get; set; } = 1;
        public int Detectability { get; set; } = 1;

        [JsonIgnore] // Newtonsoft.Json attribute
        public int Rpn => Severity * Occurrence * Detectability;
    }

    public class Edge
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid From { get; set; }
        public Guid To { get; set; }
    }

    public class Diagram
    {
        public List<Node> Nodes { get; set; } = new List<Node>();
        public List<Edge> Edges { get; set; } = new List<Edge>();
    }
}
