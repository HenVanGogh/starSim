using UnityEngine;

/// <summary>
/// Enum of different types of clickable objects in the game
/// </summary>
public enum ClickableObjectType
{
    Star,
    Planet,
    Ship,
    Station,
    UI,
    Other
}

/// <summary>
/// Interface for all objects that can be clicked in the game
/// Implements this interface on any object that should respond to clicks
/// </summary>
public interface IClickableObject
{
    /// <summary>
    /// Whether this object is currently selected
    /// </summary>
    bool isSelected { get; set; }
    
    /// <summary>
    /// Gets the type of object this is
    /// </summary>
    ClickableObjectType GetObjectType();
    
    /// <summary>
    /// Toggle the selection state of this object
    /// </summary>
    void ToggleSelection();
    
    /// <summary>
    /// Set the selected state of this object
    /// </summary>
    void SetSelected(bool selected, bool updateVisuals = true);
}