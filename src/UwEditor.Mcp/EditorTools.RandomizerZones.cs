using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace UwEditor.Mcp
{
    /// <summary>Randomizer > Scatter > Zones (UW1 and UW2): the zoning the solver and the key scatter use, its zones, painting, links, grants and key items.</summary>
    public sealed partial class EditorTools
    {
        const string RzZoneWork = " In the editor's window it is the editor's zoning (its Zones window and the view's colours follow); not written until zones_file save.";

        [McpServerTool(Name = "zones_info"), Description("The zoning (the zones the randomizer's solver uses): its file, unsaved changes, counts of zones, links and grants, the start zone, the squares that end the game, open squares in no zone per level.")]
        public CallToolResult ZonesInfo() { return Answer(() => ed.ZonesInfo()); }

        [McpServerTool(Name = "zones_list"), Description("Every zone of the zoning: id, name, tiles, levels, colour, status, start, ends the game, notes.")]
        public CallToolResult ZonesList() { return Answer(() => ed.ZonesList()); }

        [McpServerTool(Name = "zone_get"), Description("One zone: its fields, its links, its grants (numbered) and the key items lying in it.")]
        public CallToolResult ZoneGet([Description("The zone's id (UW2: level*256+n, as zones_list shows L<level>:<n>).")] int zone) { return Answer(() => ed.ZoneGet(zone)); }

        [McpServerTool(Name = "zone_map"), Description("A level's zone map as text: north (y 63) at the top, each tile its zone number in hex (## rock, .. open in no zone).")]
        public CallToolResult ZoneMap([Description("Level number, from 1.")] int level) { return Answer(() => ed.ZoneMap(level)); }

        [McpServerTool(Name = "zone_add"), Description("Adds a zone (UW2: a zone of the level given, as each level has its own)." + RzZoneWork)]
        public CallToolResult ZoneAdd([Description("Its name (default Zone <id>).")] string name = null, [Description("UW2: the level it is for.")] int level = 0)
        {
            return Answer(() => ed.ZoneAdd(name, level));
        }

        [McpServerTool(Name = "zone_set"), Description("Changes a zone's name, colour (#RRGGBB; empty for its default colour), notes, status (done, unfinished...) or levels text." + RzZoneWork)]
        public CallToolResult ZoneSet([Description("The zone's id.")] int zone, [Description("name, colour, notes, status or levels.")] string field, [Description("The new value.")] string value)
        {
            return Answer(() => ed.ZoneSetField(zone, field, value));
        }

        [McpServerTool(Name = "zone_remove"), Description("Removes a zone: its tiles become unassigned, its links and grants go (zones_undo brings back the tiles only)." + RzZoneWork)]
        public CallToolResult ZoneRemove([Description("The zone's id.")] int zone) { return Answer(() => ed.ZoneRemove(zone)); }

        [McpServerTool(Name = "zone_paint"), Description("Paints a rectangle of a level's tiles as a zone (corners in any order; rock stays rock), one step of zones_undo." + RzZoneWork)]
        public CallToolResult ZonePaint([Description("Level number, from 1.")] int level, [Description("A corner, 0-63.")] int x1, [Description("A corner, 0-63.")] int y1,
            [Description("The other corner, 0-63.")] int x2, [Description("The other corner, 0-63.")] int y2, [Description("The zone's id.")] int zone)
        {
            return Answer(() => ed.ZonePaint(level, x1, y1, x2, y2, zone));
        }

        [McpServerTool(Name = "zone_fill"), Description("Fills with a zone the area reachable from a square that shares its zone (four ways, no rock, no door tiles, no climb above the limit; drops are fine), as the Zones window's fill." + RzZoneWork)]
        public CallToolResult ZoneFill([Description("Level number, from 1.")] int level, [Description("0-63.")] int x, [Description("0-63.")] int y, [Description("The zone's id.")] int zone)
        {
            return Answer(() => ed.ZoneFill(level, x, y, zone));
        }

        [McpServerTool(Name = "zones_undo"), Description("Takes back the last painting, fill or zone removal of the zoning (Undo painting).")]
        public CallToolResult ZonesUndo() { return Answer(() => ed.ZonesUndo()); }

        [McpServerTool(Name = "zone_links"), Description("The zoning's links (the ways between zones and what each requires), or one zone's.")]
        public CallToolResult ZoneLinks([Description("A zone's id; leave out for every link.")] int? zone = null) { return Answer(() => ed.ZoneLinks(zone)); }

        [McpServerTool(Name = "zone_link_add"), Description("A new link between two zones; its requirement and kind come from any doors between them (else free, to check)." + RzZoneWork)]
        public CallToolResult ZoneLinkAdd([Description("From zone.")] int from, [Description("To zone.")] int to, [Description("Both ways (default true).")] bool twoWay = true)
        {
            return Answer(() => ed.ZoneLinkAdd(from, to, twoWay));
        }

        [McpServerTool(Name = "zone_link_set"), Description("Changes a link: from, to, kind (walk door portcullis secret doorman drop climb teleport lever stairs jump other), requires (the requirement language: free, key:5, item:x & flag:y, ...), twoWay, notes, confidence (certain, todo), once, level, x, y." + RzZoneWork)]
        public CallToolResult ZoneLinkSet([Description("The link's id.")] int link, [Description("The field.")] string field, [Description("The new value.")] string value)
        {
            return Answer(() => ed.ZoneLinkSet(link, field, value));
        }

        [McpServerTool(Name = "zone_link_remove"), Description("Removes a link." + RzZoneWork)]
        public CallToolResult ZoneLinkRemove([Description("The link's id.")] int link) { return Answer(() => ed.ZoneLinkRemove(link)); }

        [McpServerTool(Name = "zone_grant_add"), Description("A zone grants a token when reached (key:5, item:medallion, flag:gazer_dead, talisman:cup, npc:...), optionally only when a requirement holds." + RzZoneWork)]
        public CallToolResult ZoneGrantAdd([Description("The zone's id.")] int zone, [Description("The token granted.")] string grants,
            [Description("Granted only when this holds too (optional).")] string requires = null, [Description("Notes (optional).")] string notes = null)
        {
            return Answer(() => ed.ZoneGrantAdd(zone, grants, requires, notes));
        }

        [McpServerTool(Name = "zone_grant_remove"), Description("Removes one of a zone's grants by its number in zone_get." + RzZoneWork)]
        public CallToolResult ZoneGrantRemove([Description("The zone's id.")] int zone, [Description("The grant's number, from 0.")] int number)
        {
            return Answer(() => ed.ZoneGrantRemove(zone, number));
        }

        [McpServerTool(Name = "zone_grants_suggest"), Description("Adds to a zone the grants its key items suggest (the Zones window's Add them to the zone's grants)." + RzZoneWork)]
        public CallToolResult ZoneGrantsSuggest([Description("The zone's id.")] int zone) { return Answer(() => ed.ZoneGrantsSuggest(zone)); }

        [McpServerTool(Name = "zone_key_items"), Description("The key items found in the levels (keys, talismans, runes, quest items...) with their place and zone; or those in one zone.")]
        public CallToolResult ZoneKeyItems([Description("A zone's id; leave out for every key item.")] int? zone = null) { return Answer(() => ed.ZoneKeyItems(zone)); }

        [McpServerTool(Name = "zone_mark_item"), Description("Marks an object as a key item (true), as not one (false), or takes the mark away (leave important out).")]
        public CallToolResult ZoneMarkItem([Description("Level number, from 1.")] int level, [Description("The object's index (#).")] int index,
            [Description("true, false, or leave out to unmark.")] bool? important = null)
        {
            return Answer(() => ed.ZoneMarkItem(level, index, important));
        }

        [McpServerTool(Name = "zone_doors"), Description("The doors, portcullises and secret doors between zones and what each needs (a key, a lever...).")]
        public CallToolResult ZoneDoors([Description("A level; leave out for all.")] int? level = null) { return Answer(() => ed.ZoneDoorsList(level)); }

        [McpServerTool(Name = "zones_set_start"), Description("Sets the zoning's start zone (the zone of the start square).")]
        public CallToolResult ZonesSetStart([Description("The zone's id.")] int zone) { return Answer(() => ed.ZonesSetStart(zone)); }

        [McpServerTool(Name = "zones_split"), Description("Split multi-level zones by level for the solver (each level its own zone, joined by free two-way links); the zoning itself is not changed.")]
        public CallToolResult ZonesSplit([Description("On or off.")] bool on) { return Answer(() => ed.ZonesSplit(on)); }

        [McpServerTool(Name = "zone_mark_goal"), Description("Mark as goal: puts a square that ends the game on a level (a move trigger and the end-of-game hack trap), a level edit undo takes away.")]
        public CallToolResult ZoneMarkGoal([Description("Level number, from 1.")] int level, [Description("0-63.")] int x, [Description("0-63.")] int y)
        {
            return Answer(() => ed.ZoneMarkGoal(level, x, y));
        }

        [McpServerTool(Name = "zones_file"), Description("The zoning's file: new (an empty zoning), open (a zone file .json), save (to its file, DATA\\uw1-zones.json, or another; an old file kept once as .BKP), example (UW1: the zoning shipped with the editor, in its window).")]
        public CallToolResult ZonesFile([Description("new, open, save or example.")] string action, [Description("open: the file; save: another file (optional).")] string path = null,
            [Description("new, open, example: drop unsaved changes of the zoning.")] bool discard = false)
        {
            return Answer(() => ed.ZonesFile(action, path, discard));
        }

        [McpServerTool(Name = "zone_picture"), Description("A level's zones as a picture file (export), or painted from one (import), as the Zones window's picture export and import.")]
        public CallToolResult ZonePicture([Description("Level number, from 1.")] int level, [Description("export or import.")] string action, [Description("The picture file.")] string path,
            [Description("export: write over an existing file.")] bool overwrite = false)
        {
            return Answer(() => ed.ZonePicture(level, action, path, overwrite));
        }
    }
}
