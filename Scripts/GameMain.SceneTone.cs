using Godot;

namespace Tenshi;

public partial class GameMain
{
    private ColorRect _sceneTone = null!;
    private ShaderMaterial _sceneToneMaterial = null!;
    private int _sceneToneId;

    private void BuildSceneTone()
    {
        _sceneToneMaterial = new ShaderMaterial { Shader = new Shader { Code = """
            shader_type canvas_item;
            uniform sampler2D screen_texture : hint_screen_texture, filter_nearest, repeat_disable;
            uniform bool inverse = false;
            void fragment() {
                // Native 100154F0 replicates the GREEN byte and saturating-adds 32;
                // 10015570 then XORs RGB with 255 for the inverted variant.
                float value = min(1.0, texture(screen_texture, SCREEN_UV).g + 32.0 / 255.0);
                if (inverse) value = 1.0 - value;
                COLOR = vec4(vec3(value), 1.0);
            }
            """ } };
        _sceneTone = new ColorRect { Size = new Vector2(800, 600), Visible = false,
            MouseFilter = MouseFilterEnum.Ignore, Material = _sceneToneMaterial };
        _game.AddChild(_sceneTone);
    }

    private void SetSceneTone(int effect)
    {
        _sceneToneId = effect;
        _sceneTone.Visible = effect is 42 or 43;
        _sceneToneMaterial.SetShaderParameter("inverse", effect == 43);
    }
}
