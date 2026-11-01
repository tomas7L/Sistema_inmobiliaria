using System.Security.Cryptography;

namespace Inmobiliaria.Domain.Access;

/// <summary>
/// The password the system gives a user it creates, resets or reactivates (gestion-de-usuarios spec
/// Requirement 6, design D6): three words and five digits, such as <c>lago-sol-pan-48017</c>, easy to
/// dictate over the phone. 3 × log2(256) + 5 × log2(10) is about 40.6 bits (spec Decision 13). A word
/// block never holds a digit and the digit block never holds a letter, so "l" and "1", "o" and "0" are
/// never confused.
/// </summary>
public static class ProvisionalPassword
{
    private const int WordCount = 3;
    private const int DigitCount = 5;

    /// <summary>Everyday Spanish words, 3 to 6 letters, a to z only: no "ñ" or accents to type.</summary>
    public static IReadOnlyList<string> Words { get; } =
    [
        "agua", "aire", "ala", "alba", "alma", "amigo", "ancla", "anillo",
        "arbol", "arco", "arena", "arroz", "astro", "aula", "avena", "azul",
        "baile", "balsa", "banco", "barco", "barro", "beso", "bici", "bloque",
        "boca", "bola", "bolsa", "bosque", "bote", "brazo", "brisa", "broche",
        "bruma", "buzo", "cabra", "cacao", "cafe", "caja", "calle", "cama",
        "campo", "canal", "canoa", "cancha", "capa", "cara", "carta", "casa",
        "cebra", "cedro", "cena", "cerro", "cielo", "cine", "cinta", "circo",
        "clavo", "coco", "cofre", "colina", "color", "copa", "coral", "corona",
        "cuadro", "cubo", "cuento", "cuerda", "dado", "delta", "diente", "disco",
        "domo", "duna", "duende", "eco", "eje", "faro", "feria", "fideo",
        "fiesta", "flor", "foca", "foco", "fondo", "fresa", "fruta", "fuego",
        "gaita", "gallo", "ganso", "garza", "gato", "gema", "globo", "gorra",
        "gota", "grano", "grillo", "gruta", "guante", "haba", "hada", "hielo",
        "hierro", "higo", "hilo", "hoja", "horno", "huevo", "humo", "isla",
        "jardin", "jarra", "jugo", "jirafa", "kiwi", "lago", "lana", "lapiz",
        "lazo", "leche", "leon", "libro", "lima", "limon", "lino", "llave",
        "lluvia", "lobo", "loma", "loro", "luna", "luz", "mago", "maiz",
        "malva", "mango", "mano", "manta", "mapa", "mar", "marco", "mate",
        "mesa", "miel", "mimbre", "mono", "monte", "mora", "mula", "muro",
        "nabo", "nave", "nido", "niebla", "nieve", "nube", "nuez", "oasis",
        "ola", "olivo", "olla", "oro", "oso", "oveja", "pala", "palma",
        "pan", "panda", "papel", "pasto", "pato", "pera", "perla", "pez",
        "piano", "pino", "pipa", "plato", "playa", "pluma", "polvo", "poncho",
        "potro", "prado", "puente", "puerta", "pulpo", "queso", "radio", "rama",
        "rana", "rayo", "red", "regla", "reloj", "remo", "rio", "roble",
        "roca", "rosa", "rueda", "sal", "salto", "sapo", "selva", "silla",
        "sol", "sombra", "sopa", "taco", "tambor", "taza", "techo", "tejado",
        "tela", "tigre", "tinta", "toro", "torre", "trigo", "trompo", "tronco",
        "tuna", "uva", "vaca", "valle", "vapor", "vela", "velero", "verde",
        "vaso", "viento", "violin", "volcan", "yate", "yerba", "yeso", "yoyo",
        "zorro", "zapato", "abeja", "acero", "album", "ancho", "antena", "avion",
        "bahia", "banda", "barril", "baul", "bomba", "borde", "botin", "cable",
    ];

    public static string Generate() => Generate(RandomNumberGenerator.GetInt32);

    /// <summary>Builds a password from <paramref name="next"/>, which returns a value in [0, bound).</summary>
    public static string Generate(Func<int, int> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        var parts = new List<string>(WordCount + 1);
        for (var i = 0; i < WordCount; i++)
        {
            parts.Add(Words[next(Words.Count)]);
        }

        var digits = new char[DigitCount];
        for (var i = 0; i < DigitCount; i++)
        {
            digits[i] = (char)('0' + next(10));
        }

        parts.Add(new string(digits));
        return string.Join('-', parts);
    }
}
