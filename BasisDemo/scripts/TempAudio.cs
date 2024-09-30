using System.Collections;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class TempAudio : MonoBehaviour
{
    public AudioClip[] sounds;

    public void Play()
    {
        StartCoroutine(PlayCo());
    }

    public IEnumerator PlayCo()
    {
        if (TryGetComponent(out AudioSource src))
        {
            src.clip = sounds[Random.Range(0, sounds.Length)];
            src.Play();
            yield return new WaitForSeconds(src.clip.length);
        }
        Destroy(gameObject);
    }
}