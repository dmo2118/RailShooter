using System;
using System.Diagnostics;
using System.Drawing;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using OpenTK.Input;
using System.Collections.Generic;

// Mostly copied-and-pasted from the OpenTK introduction.

// OpenGL 1.1, because
// a. I don't have the patience to compile shaders for everything.
// b. It works on really really old hardware.

namespace TestGameSharp
{
    class Engine : GameWindow
    {
        struct TexInfo
        {
            public int width, height;
        }

        static TexInfo LoadTexture(String path, int tex2d)
        {
            GL.BindTexture(TextureTarget.Texture2D, tex2d);

            using (var bitmap = new Bitmap(path))
            {
                var data = bitmap.LockBits(
                    new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    System.Drawing.Imaging.ImageLockMode.ReadOnly,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                try
                {
                    // TODO: Take Stride into account!
                    Debug.Assert(data.Stride == data.Width * 4);

                    GL.TexImage2D(
                        TextureTarget.Texture2D,
                        0,
                        PixelInternalFormat.Rgba,
                        bitmap.Width,
                        bitmap.Height,
                        0,
                        PixelFormat.Bgra,
                        PixelType.UnsignedByte,
                        data.Scan0);

                    GL.TexParameter(
                        TextureTarget.Texture2D, 
                        TextureParameterName.TextureMinFilter, 
                        (int)TextureMinFilter.Linear);

                    GL.TexParameter(
                        TextureTarget.Texture2D, 
                        TextureParameterName.TextureMagFilter, 
                        (int)TextureMagFilter.Linear);

                    return new TexInfo { width = bitmap.Width, height = bitmap.Height };
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }
            }
        }

        enum Textures
        {
            Backdrop,
            Title,
            Ship,
            Boom0,
            Boom1,
            Boom2,
            HateShroom,
            RedShot,
            BlueShot
        }

        readonly static string[] tex_paths = new string[]
        {
            @"res\backdrop.jpg",
            @"res\title.png",
            @"res\ship.png",
            @"res\boom0.png",
            @"res\boom1.png",
            @"res\boom2.png",
            @"res\hateshroom.png",
            @"res\redshot.png",
            @"res\blueshot.png"
        };

        struct Boom
        {
            public const uint frame_length = 4;
            public const uint boom_count = 3;

            public uint start_frame;
            public PointF pos;
        }

        class HateShroom
        {
            public uint start_frame;
            public PointF pos;
            public PointF delta;
        };

        class Shot
        {
            public PointF pos;
            public PointF delta;
        }

        const int title_frames = 120;
        const int fadeout_frames = 60;

        int[] tex_objs = new int[tex_paths.Length]; // Separate from tex_info for the sake of GenTextures.
        TexInfo[] tex_info = new TexInfo[tex_paths.Length];

        uint frame = 0;
        PointF ship_pos;
        uint? shipwreck = null;
        bool first_run = true;

        Random rand = new Random();

        List<Boom> booms = new List<Boom>();
        List<HateShroom> shrooms = new List<HateShroom>();
        List<Shot> shots_red = new List<Shot>();
        List<Shot> shots_blue = new List<Shot>();

        Engine() : 
            base(
                1024, 
                768, 
                OpenTK.Graphics.GraphicsMode.Default, 
                "Uninspired Rail Shooter" /*,
                GameWindowFlags.Fullscreen */)
        {
        }

        void Sprite(PointF pt, Textures tex)
        {
            float scale = 1 / (640.0f * 2.0f);
            int tex_id = (int)tex;
            float dx = scale * tex_info[tex_id].width;
            float dy = scale * tex_info[tex_id].height;

            GL.BindTexture(TextureTarget.Texture2D, tex_objs[tex_id]);

            GL.Begin(PrimitiveType.Quads);

            GL.TexCoord2(0.0f, 0.0f);
            GL.Vertex2(pt.X - dx, pt.Y - dy);

            GL.TexCoord2(0.0f, 1.0f);
            GL.Vertex2(pt.X - dx, pt.Y + dy);

            GL.TexCoord2(1.0f, 1.0f);
            GL.Vertex2(pt.X + dx, pt.Y + dy);

            GL.TexCoord2(1.0f, 0.0f);
            GL.Vertex2(pt.X + dx, pt.Y - dy);

            GL.End();
        }

        double YSize()
        {
            return Height / (Width * 2.0);
        }

        uint BoomFrame(Boom b)
        {
            return (frame - b.start_frame) / Boom.frame_length;
        }

        static bool OutOfBounds(PointF pt)
        {
            return !(pt.X > -0.05 && pt.X < 1.05 && pt.Y > -1.5 && pt.Y < 1.5);
        }

        void DoLoad(object sender, EventArgs e)
        {
            VSync = VSyncMode.On;

            Console.Out.WriteLine(GL.GetString(StringName.Vendor));
            Console.Out.WriteLine(GL.GetString(StringName.Renderer));
            Console.Out.WriteLine(GL.GetString(StringName.Version));

            GL.GenTextures(tex_objs.Length, tex_objs);
            for(int i = 0; i != tex_paths.Length; ++i)
                tex_info[i] = LoadTexture(tex_paths[i], tex_objs[i]);

            Debug.Assert(GL.GetError() == ErrorCode.NoError);

            // GL state init
            GL.Enable(EnableCap.Texture2D);
            GL.BlendFunc(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha);
        }

        void DoResize(object sender, EventArgs e)
        {
            GL.Viewport(0, 0, Width, Height);

            GL.MatrixMode(MatrixMode.Projection);
            GL.LoadIdentity();
            var y_size = YSize();
            GL.Ortho(0, 1, y_size, -y_size, 1, -1); // Not sure if zNear/zFar is OK enough.
        }

        static void MiniRemove<T>(List<T> list, Func<T, bool> remove_pred)
        {
            for (int i = 0; i < list.Count; ++i)
            {
                if (remove_pred(list[i]))
                {
                    list[i] = list[list.Count - 1];
                    list.RemoveAt(list.Count - 1);
                }
            }
        }

        static PointF PointAdd(PointF p0, PointF p1)
        {
            return new PointF(p0.X + p1.X, p0.Y + p1.Y);
        }

        static PointF PointSub(PointF p0, PointF p1)
        {
            return new PointF(p0.X - p1.X, p0.Y - p1.Y);
        }

        static PointF PointMul(PointF p, float fac)
        {
            return new PointF(p.X * fac, p.Y * fac);
        }

        static float PointHypot(PointF p)
        {
            return p.X * p.X + p.Y * p.Y;
        }

        void AddBoom(PointF pos)
        {
            booms.Add(new Boom { start_frame = frame, pos = pos });
        }

        void DoUpdateFrame(object sender, FrameEventArgs args)
        {
            if (shipwreck.HasValue && frame >= shipwreck.Value + fadeout_frames)
            {
                shipwreck = null;
                frame = 0;
                first_run = false;
                shrooms = new List<HateShroom>();
                shots_red = new List<Shot>();
                shots_blue = new List<Shot>();
            }

            ++frame; // Important because of Booms.

            if (Keyboard[Key.Escape])
                Exit();

            // No controls other than moving the mouse.
            var mouse = Mouse;
            ship_pos.X = mouse.X / (float)Width;
            ship_pos.Y = (mouse.Y * 2 - Height) / 2.0f / Width;

            // TODO: Keep score or something!

            // Booms
            MiniRemove(booms, (boom) => { return BoomFrame(boom) >= Boom.boom_count; });
            /*
            if (frame % 10 == 0)
                AddBoom(new PointF((float)rand.NextDouble(), (float)rand.NextDouble()));
            */

            // New shrooms
            if (frame % 60 == 0 && frame >= title_frames)
            {
                double max_dy = 0.01f;
                shrooms.Add(new HateShroom
                {
                    start_frame = frame,
                    pos = new PointF(1.0f, (float)(rand.NextDouble() - 0.5f)),
                    delta = new PointF(-0.005f, (float)(rand.NextDouble() * max_dy - max_dy * 0.5))
                });
            }

            // Old shrooms
            MiniRemove(shrooms, (shroom) => { return shroom.pos.X < 0; });

            // Handle shrooms. Also, check if we should fire.
            bool ship_firing = false;
            foreach(var shroom in shrooms)
            {
                shroom.pos = PointAdd(shroom.pos, shroom.delta);

                if(!(shipwreck.HasValue) && (frame - shroom.start_frame) % 40 == 0)
                {
                    PointF d = PointSub(shroom.pos, ship_pos);
                    float speed = (float)(-0.005 / Math.Sqrt(PointHypot(d)));
                    d = PointMul(d, speed);
                    shots_red.Add(new Shot { pos = shroom.pos, delta = d });
                }

                if(Math.Abs(ship_pos.Y - shroom.pos.Y) < 0.1)
                    ship_firing = true;
            }

            // Handle shots
            MiniRemove(shots_red, (shot) => { return OutOfBounds(shot.pos); });
            MiniRemove(shots_blue, (shot) => { return OutOfBounds(shot.pos); });

            foreach (var shot in shots_red)
                shot.pos = PointAdd(shot.pos, shot.delta);
            foreach (var shot in shots_blue)
                shot.pos = PointAdd(shot.pos, shot.delta);

            // Fire!
            if (!shipwreck.HasValue && ship_firing && frame % 5 == 0)
                shots_blue.Add(new Shot { pos = ship_pos, delta = new PointF(0.05f, 0) });
        }

        void Gradient(double a0, double a1)
        {
            GL.Begin(PrimitiveType.Quads);
            float y_size = (float)YSize();
            GL.Color4(0.0f, 0.0f, 0.0f, a0);
            GL.Vertex2(0.0f, -y_size);
            GL.Vertex2(0.0f, y_size);
            GL.Color4(0.0f, 0.0f, 0.0f, a1);
            GL.Vertex2(1.0f, y_size);
            GL.Vertex2(1.0f, -y_size);
            GL.End();
        }

        void DoRenderFrame(object sender, FrameEventArgs e)
        {
            // render graphics
            GL.Clear(ClearBufferMask.ColorBufferBit);
            GL.Color4(1.0f, 1.0f, 1.0f, 1.0f);

            GL.Enable(EnableCap.Texture2D);

            GL.Disable(EnableCap.Blend);

            float now = (float)(frame / TargetUpdateFrequency);

            /* Backdrop */
            {
                GL.BindTexture(TextureTarget.Texture2D, tex_objs[(int)Textures.Backdrop]);

                float x = now / 32;
                GL.Begin(PrimitiveType.Quads);

                GL.TexCoord2(x + 0.0f, 0.0f);
                GL.Vertex2(0.0f, -0.5f);

                GL.TexCoord2(x + 0.0f, 1.0f);
                GL.Vertex2(0.0f, 0.5f);

                GL.TexCoord2(x + 1.0f, 1.0f);
                GL.Vertex2(1.0f, 0.5f);

                GL.TexCoord2(x + 1.0f, 0.0f);
                GL.Vertex2(1.0f, -0.5f);

                GL.End();
            }

            GL.Enable(EnableCap.Blend);

            /* Shots */
            foreach (var shot in shots_red)
            {
                Sprite(shot.pos, Textures.RedShot);
                if (!shipwreck.HasValue && PointHypot(PointSub(shot.pos, ship_pos)) < 0.001)
                {
                    AddBoom(ship_pos);
                    shipwreck = frame;
                }
            }

            foreach (var shot in shots_blue)
            {
                Sprite(shot.pos, Textures.BlueShot);
                foreach (var shroom in shrooms)
                {
                    if (PointHypot(PointSub(shot.pos, shroom.pos)) < 0.005)
                    {
                        AddBoom(shroom.pos);
                        shroom.pos = new PointF(-1, 0); // Cheating!
                    }
                }
            }

            /* The ship! */
            if (!shipwreck.HasValue)
                Sprite(ship_pos, Textures.Ship);

            /* Booms */
            foreach(var boom in booms)
                Sprite(boom.pos, (Textures)((uint)Textures.Boom0 + BoomFrame(boom)));

            /* HateShrooms */
            foreach (var shroom in shrooms)
                Sprite(shroom.pos, Textures.HateShroom);

            // Below this point changes the GL color.
            if(frame < title_frames && first_run)
            {
                float alpha = 4 * (1.0f - (frame / (float)title_frames));
                GL.Color4(1.0f, 1.0f, 1.0f, alpha);
                Sprite(new PointF(0.5f, 0), Textures.Title);
            }

            GL.Disable(EnableCap.Texture2D);

            if (shipwreck.HasValue)
            {
                double t = (frame - shipwreck.Value);
                Gradient((t + 20 - fadeout_frames) / 10.0f, (t + 10 - fadeout_frames) / 10.0f);
            }

            if (frame < title_frames && !first_run)
            {
                double t = frame;
                Gradient(1 - (t / 10.0f), 1 - ((t - 10) / 10.0f));
            }

            SwapBuffers();

            Debug.Assert(GL.GetError() == ErrorCode.NoError);
        }

        static void Main()
        {
            using (var game = new Engine())
            {
                game.CursorVisible = false;

                game.Load += game.DoLoad;
                game.Resize += game.DoResize;
                game.UpdateFrame += game.DoUpdateFrame;
                game.RenderFrame += game.DoRenderFrame;

                // TODO: How are we supposed to get the refresh rate?
                game.Run(60.0);
            }
        }
    }
}
