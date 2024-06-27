using System.Text.Json;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;

namespace MapRestrictions;

public class MapRestrictions : BasePlugin
{
    public override string ModuleAuthor => "NiGHT";
    public override string ModuleName => "MapRestrictions";
    public override string ModuleDescription => "Restrict maps to certain players";
    public override string ModuleVersion => "0.0.1";
    
    public class MapData
    {
	    public string? MapName { get; set; }
	    public MapRestriction Restrictions { get; set; } = new();
    }

    public class MapRestriction
    {
	    public string Model { get; set; } = string.Empty;
	    public Dictionary<string, MapMessages> Messages { get; set; } = new();
	    public Dictionary<string, MapArea> Areas { get; set; } = new();
    }

    public class MapMessages
    {
	    public int LessThan { get; set; }
	    public int MoreThan { get; set; }
	    public string? Message { get; set; }
    }

    public class MapArea
    {
	    public int LessThan { get; set; }
	    public int MoreThan { get; set; }
	    public string? Origin { get; set; }
	    public string? Angles { get; set; }
	    
	    public string? Scale { get; set; }
    }
    
    private string? _path;
    private CCSGameRules? _gameRules;
    private MapData? _mapData;
    private readonly HashSet<CBaseModelEntity> _spawnedProps = new();
    
    private static CCSGameRules GetGameRules()
    {
	    return Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules!;
    }
    
    private static Vector StringToVector(string vector)
	{
	    var split = vector.Split(' ');
	    return new Vector(float.Parse(split[0]), float.Parse(split[1]), float.Parse(split[2]));
	}
    
	private static QAngle StringToQAngle(string vector)
	{
		var split = vector.Split(' ');
		return new QAngle(float.Parse(split[0]), float.Parse(split[1]), float.Parse(split[2]));
	}
    
    private void SpawnProp(string modelPath, Vector origin, QAngle angles, float scale = 0.0f)
    {
	    var prop = Utilities.CreateEntityByName<CBaseModelEntity>("prop_dynamic_override");

	    if (prop == null) 
		    return;

	    prop.Collision.SolidType = SolidType_t.SOLID_VPHYSICS;
	    prop.Teleport(origin, angles, new Vector(0, 0, 0));
	    prop.DispatchSpawn();
	    Server.NextFrame(() => prop.SetModel(modelPath));
	    
	    _spawnedProps.Add(prop);
	    
	    if(scale == 0.0f)
		    return;
	    
	    var bodyComponent = prop.CBodyComponent;
	    if (bodyComponent is not { SceneNode: not null })
		    return;
	    
	    bodyComponent.SceneNode.GetSkeletonInstance().Scale = scale;
    }
    
    private void SpawnProps()
    {
	    if(_mapData == null || _gameRules == null || _gameRules.WarmupPeriod)
		    return;

	    var players = Utilities.GetPlayers().Where(x => x.Connected == PlayerConnectedState.PlayerConnected)
		    .ToList();
	    var playersConnected = players.Count;
	    
	    var message = string.Empty;
	    // find the message to send
	    
	    for (var i = 0; i < _mapData.Restrictions.Messages.Count; i++)
	    {
		    var mapMessage = _mapData.Restrictions.Messages.ElementAt(i).Value;
		    if (playersConnected > mapMessage.MoreThan && (mapMessage.LessThan == 0 || playersConnected < mapMessage.LessThan))
				message = mapMessage.Message;
	    }
	    
	    if (!string.IsNullOrEmpty(message))
	    {
		    // count ct and t players
		    var ctPlayers = players.Where(x => x is { Team: CsTeam.CounterTerrorist}).ToList().Count;
		    var tPlayers = players.Where(x => x is { Team: CsTeam.Terrorist}).ToList().Count;
		    
		    Server.PrintToChatAll(Localizer["StartMessage"].Value.Replace("{tPlayers}", tPlayers.ToString()).Replace("{ctPlayers}", ctPlayers.ToString()).Replace("{message}", message));
	    }
	    
	    // now let's spawn the props based on
		var model = _mapData.Restrictions.Model;
		for (var i = 0; i < _mapData.Restrictions.Areas.Count; i++)
		{
			var mapArea = _mapData.Restrictions.Areas.ElementAt(i).Value;
			if (playersConnected > mapArea.MoreThan && (mapArea.LessThan == 0 || playersConnected < mapArea.LessThan))
			{
				if(mapArea is { Origin: not null, Angles: not null })
					SpawnProp(model, StringToVector(mapArea.Origin), StringToQAngle(mapArea.Angles), string.IsNullOrEmpty(mapArea.Scale) ? 0.0f : float.Parse(mapArea.Scale));
			}
		}
    }

    
    private void LoadConfig(string name)
    {
	    if (_mapData != null)
	    {
		    _mapData.Restrictions.Areas.Clear();
		    _mapData.Restrictions.Messages.Clear();
		    _mapData.Restrictions.Model = string.Empty;

		    _mapData = null;
	    }
	    
	    _path = ModuleDirectory + "/configs/" + name + ".json";
	    Console.WriteLine($"{ModuleName} LoadConfig - Loading map data from {_path}");
	    if (!File.Exists(_path))
	    {
		    Console.WriteLine($"{ModuleName} LoadConfig - No map data found for {_path}");
		    return;
	    }
			
	    Console.WriteLine($"{ModuleName} LoadConfig - Found map data for {_path}");
	    try
	    {
		    _mapData = JsonSerializer.Deserialize<MapData>(File.ReadAllText(_path));
	    }
	    catch (Exception ex)
	    {
		    // Handle any potential JSON parsing exceptions
		    Console.WriteLine($"{ModuleName} LoadConfig - Error loading map data: {ex.Message}");
	    }
    }

    public override void Load(bool hotReload)
    {
	    RegisterListener<Listeners.OnMapStart>(LoadConfig);

	    if (!hotReload || _mapData != null)
		    return;
	    
	    var name = Server.MapName;
	    if(string.IsNullOrEmpty(name))
		    return;
		    
	    try
	    {
		    LoadConfig(name);
	    }
	    catch (Exception ex)
	    {
		    Console.WriteLine($"{ModuleName} Load - Error loading map data: {ex.Message}");
	    }
    }

    public override void Unload(bool hotReload)
    {
	    foreach (var index in _spawnedProps.OfType<CBaseModelEntity>().Where(index => index.IsValid))
		    index.Remove();
	    
	    _spawnedProps.Clear();
    }

    [GameEventHandler]
    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
	    if (_mapData == null)
		    return HookResult.Continue;
	    
	    _spawnedProps.Clear();
	    _gameRules = GetGameRules();
	    
	    SpawnProps();
	    return HookResult.Continue;
    }
    
    [ConsoleCommand("maprestrictions_reload")]
    [RequiresPermissions("@css/root")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void ReloadMapRestrictions(CCSPlayerController? caller, CommandInfo command)
	{
		foreach (var index in _spawnedProps.OfType<CBaseModelEntity>().Where(index => index.IsValid))
		{
			//Console.WriteLine($"{ModuleName} removed prop {index.Handle} + {index.DesignerName} + {index.Index}");
			index.Remove();
		}
		
		_spawnedProps.Clear();
		
	    if (_path == null)
		    return;
	    
	    try
	    {
		    _mapData = JsonSerializer.Deserialize<MapData>(File.ReadAllText(_path));
	    }
	    catch (Exception ex)
	    {
		    // Handle any potential JSON parsing exceptions
		    Console.WriteLine($"{ModuleName} ReloadMapRestrictions - Error loading map data: {ex.Message}");
	    }
	    
	    var newRestrictionsCount = _mapData?.Restrictions.Areas.Count;
	    var newMessagesCount = _mapData?.Restrictions.Messages.Count;
	    if(caller == null)
		    Server.PrintToConsole($"[{ModuleName}] Reloaded map restrictions for {_mapData?.MapName}, found {newRestrictionsCount} restrictions and {newMessagesCount} messages");
		else 
	    	caller.PrintToChat($"MapRestrictions - Reloaded map restrictions for {_mapData?.MapName}, found {newRestrictionsCount} restrictions and {newMessagesCount} messages");
	    
	    SpawnProps();
	}
}