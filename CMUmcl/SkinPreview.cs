// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace CMUmcl
{
    internal sealed class SkinPreview : Grid
    {
        private readonly Viewport3D _view = new();
        private readonly AxisAngleRotation3D _rotation = new(new Vector3D(0,1,0),-20);
        private readonly TextBlock _empty = new() { Text="暂无皮肤预览",HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center };
        private Point _last;
        internal double Rotation => _rotation.Angle;
        internal int Parts { get; private set; }
        internal SkinPreview()
        {
            Background=Brushes.Transparent; ClipToBounds=true;
            _empty.SetResourceReference(TextBlock.ForegroundProperty,"MutedTextBrush");
            Children.Add(_view);Children.Add(_empty);
            _view.Camera=new OrthographicCamera(new Point3D(0,17,65),new Vector3D(0,-1,-65),new Vector3D(0,1,0),28);
            MouseLeftButtonDown+=(_,e)=>{_last=e.GetPosition(this);CaptureMouse();e.Handled=true;};
            MouseMove+=(_,e)=>{if(IsMouseCaptured){var point=e.GetPosition(this);RotateBy(point.X-_last.X);_last=point;}};
            MouseLeftButtonUp+=(_,e)=>{ReleaseMouseCapture();e.Handled=true;};
        }
        internal void RotateBy(double delta) => _rotation.Angle=(_rotation.Angle+delta*0.7)%360;
        internal void SetSkin(BitmapSource? skin, bool slim)
        {
            _view.Children.Clear();Parts=0;_empty.Visibility=skin==null?Visibility.Visible:Visibility.Collapsed;
            if(skin==null)return;
            var scene=new Model3DGroup();scene.Children.Add(new AmbientLight(Color.FromRgb(145,145,145)));scene.Children.Add(new DirectionalLight(Colors.White,new Vector3D(-1,-2,-3)));
            var body=new Model3DGroup { Transform=new RotateTransform3D(_rotation,new Point3D(0,16,0)) };
            int scale=skin.PixelWidth/64;bool modern=skin.PixelHeight==skin.PixelWidth;
            void Box(double x,double y,double z,int w,int h,int d,int u,int v,double inflate=0)
            {
                Parts++;
                double left=x-w/2d-inflate,right=x+w/2d+inflate,bottom=y-h/2d-inflate,top=y+h/2d+inflate,back=z-d/2d-inflate,front=z+d/2d+inflate;
                void Face(Point3D a,Point3D b,Point3D c,Point3D e,int tx,int ty,int tw,int th)
                {
                    var crop=new CroppedBitmap(skin,new Int32Rect(tx*scale,ty*scale,tw*scale,th*scale));crop.Freeze();
                    var rgba=new FormatConvertedBitmap(crop,PixelFormats.Bgra32,null,0);
                    var source=new byte[tw*scale*th*scale*4];rgba.CopyPixels(source,tw*scale*4,0);
                    int width=tw*scale, height=th*scale;int factor=Math.Max(1,128/Math.Max(width,height));var enlarged=new byte[width*height*factor*factor*4];
                    for(int yy=0;yy<height*factor;yy++)for(int xx=0;xx<width*factor;xx++)Buffer.BlockCopy(source,((yy/factor)*width+xx/factor)*4,enlarged,(yy*width*factor+xx)*4,4);
                    var texture=BitmapSource.Create(width*factor,height*factor,96,96,PixelFormats.Bgra32,null,enlarged,width*factor*4);texture.Freeze();
                    var brush=new ImageBrush(texture);RenderOptions.SetBitmapScalingMode(brush,BitmapScalingMode.NearestNeighbor);
                    var mesh=new MeshGeometry3D { Positions=new Point3DCollection { a,b,c,e },TriangleIndices=new Int32Collection {0,1,2,0,2,3},TextureCoordinates=new PointCollection {new Point(0,1),new Point(1,1),new Point(1,0),new Point(0,0)} };
                    var material=new DiffuseMaterial(brush);body.Children.Add(new GeometryModel3D(mesh,material){BackMaterial=material});
                }
                Face(new(left,bottom,front),new(right,bottom,front),new(right,top,front),new(left,top,front),u+d,v+d,w,h);
                Face(new(right,bottom,back),new(left,bottom,back),new(left,top,back),new(right,top,back),u+2*d+w,v+d,w,h);
                Face(new(left,bottom,back),new(left,bottom,front),new(left,top,front),new(left,top,back),u,v+d,d,h);
                Face(new(right,bottom,front),new(right,bottom,back),new(right,top,back),new(right,top,front),u+d+w,v+d,d,h);
                Face(new(left,top,front),new(right,top,front),new(right,top,back),new(left,top,back),u+d,v,w,d);
                Face(new(left,bottom,back),new(right,bottom,back),new(right,bottom,front),new(left,bottom,front),u+d+w,v,w,d);
            }
            int arm=slim&&modern?3:4;double ax=4+arm/2d;
            Box(0,28,0,8,8,8,0,0);Box(0,28,0,8,8,8,32,0,0.45);
            Box(0,18,0,8,12,4,16,16);Box(-ax,18,0,arm,12,4,40,16);Box(ax,18,0,arm,12,4,modern?32:40,modern?48:16);
            Box(-2,6,0,4,12,4,0,16);Box(2,6,0,4,12,4,modern?16:0,modern?48:16);
            if(modern){Box(0,18,0,8,12,4,16,32,0.2);Box(-ax,18,0,arm,12,4,40,32,0.2);Box(ax,18,0,arm,12,4,48,48,0.2);Box(-2,6,0,4,12,4,0,32,0.2);Box(2,6,0,4,12,4,0,48,0.2);}
            scene.Children.Add(body);_view.Children.Add(new ModelVisual3D {Content=scene});
        }
    }
}
