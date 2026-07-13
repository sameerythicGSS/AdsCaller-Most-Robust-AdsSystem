using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewGameCatalog", menuName = "Ads Caller/Game Catalog")]
public class GameCatalog : ScriptableObject
{
    [Tooltip("List of all games available in this catalog.")]
    public List<GameAdEntry> gameEntries = new ();
}