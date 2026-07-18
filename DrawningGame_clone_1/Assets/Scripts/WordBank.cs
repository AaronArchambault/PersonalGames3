using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Simple static word list. Server picks from this when starting a round.
/// Expand this list or load from a text/JSON file as you like.
/// </summary>
public static class WordBank
{
    private static readonly List<string> Words = new List<string>
    {
        "apple", "banana", "car", "house", "tree", "dog", "cat", "sun",
        "moon", "star", "guitar", "piano", "robot", "rocket", "castle",
        "dragon", "pizza", "burger", "bicycle", "airplane", "umbrella",
        "rainbow", "snowman", "beach", "mountain", "computer", "phone",
        "clock", "book", "glasses", "shoe", "hat", "balloon", "kite",
        "camera", "boat", "train", "bridge", "flower", "butterfly",
        "elephant", "lion", "penguin", "octopus", "shark", "spider",
        "ghost", "wizard", "knight", "pirate"
    };

    private static List<string> _shuffledBag = new List<string>();

    /// <summary>
    /// Returns a random word, avoiding repeats until the whole list has been used once.
    /// </summary>
    public static string GetRandomWord()
    {
        if (_shuffledBag.Count == 0)
        {
            _shuffledBag = new List<string>(Words);
            // Fisher-Yates shuffle
            for (int i = _shuffledBag.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (_shuffledBag[i], _shuffledBag[j]) = (_shuffledBag[j], _shuffledBag[i]);
            }
        }

        string word = _shuffledBag[_shuffledBag.Count - 1];
        _shuffledBag.RemoveAt(_shuffledBag.Count - 1);
        return word;
    }

    /// <summary>
    /// Returns 3 random word choices for the drawer to pick from (skribbl-style).
    /// </summary>
    public static List<string> GetWordChoices(int count = 3)
    {
        var choices = new List<string>();
        for (int i = 0; i < count; i++)
            choices.Add(GetRandomWord());
        return choices;
    }
}