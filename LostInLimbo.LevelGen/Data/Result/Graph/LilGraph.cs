namespace LostInLimbo.LevelGen.Data.Result.Graph;

/// <summary>
/// <para>Abstract representation of level. Contains level design: nodes are places and relationships are passages between places.</para>
/// <para>
/// Nodes:
/// <list type="bullet">
/// <item>Represent places. Note "places" is deliberately vague; it can be small room or multiple areas.</item>
/// <item>Player has access to everything within given place.</item>
/// <item>Place can contain POI (Point of Interest). It can be <c>start</c>, <c>exit</c>, <c>goal</c>, <c>solution</c> and more.</item>
/// <item>Example of <c>solution</c>: key/button/lever used to open door.</item>
/// </list>
/// </para>
/// <para>
/// Relationships:
/// <list type="bullet">
/// <item>Represent passages between places. It can be single door or entire width of open level.</item>
/// <item>Passage can be open or conditional.</item>
/// <item>Passage can be bidirectional (most common case) or one-way.</item>
/// <item>Example of <c>condition</c>: door that requires key.</item>
/// </list>
/// </para>
/// <para>Algorithm that generates graph needs to ensure graph is possible to solve: for example, key (<c>solution</c>) is always before door that requires it (<c>condition</c>). No softlock.</para>
/// NOTE: currently placeholder.
/// </summary>
public sealed record LilGraph
{
}