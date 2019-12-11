using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FFTOcean : MonoBehaviour
{

    [Range(3, 14)]
    public int FFTPow = 10;         //生成海洋纹理大小 2的次幂，例 为10时，纹理大小为1024*1024
    public int MeshSize = 250;		//网格长宽数量
    public float MeshLength = 10;	//网格长度
    public float A = 10;			//phillips谱参数，影响波浪高度
    public float Lambda = -1;       //用来控制偏移大小
    public float HeightScale = 1;   //高度影响
    public float BubblesScale = 1;  //泡沫强度
    public float BubblesThreshold = 1;//泡沫阈值
    public float WindScale = 2;     //风强
    public float TimeScale = 1;     //时间影响
    public Vector4 WindAndSeed = new Vector4(0.1f, 0.2f, 0, 0);//风向和随机种子 xy为风, zw为两个随机种子
    public ComputeShader OceanCS;   //计算海洋的cs
    public Material OceanMaterial;  //渲染海洋的材质
    public Material DisplaceXMat;   //x偏移材质
    public Material DisplaceYMat;   //y偏移材质
    public Material DisplaceZMat;   //z偏移材质
    public Material DisplaceMat;    //偏移材质
    public Material NormalMat;      //法线材质
    public Material BubblesMat;     //泡沫材质
    [Range(0, 12)]
    public int ControlM = 12;       //控制m,控制FFT变换阶段
    public bool isControlH = true;  //是否控制横向FFT，否则控制纵向FFT


    private int fftSize;			//fft纹理大小 = pow(2,FFTPow)
    private float time = 0;             //时间

    private int[] vertIndexs;		//网格三角形索引
    private Vector3[] positions;    //位置
    private Vector2[] uvs; 			//uv坐标
    private Mesh mesh;
    private MeshFilter filetr;
    private MeshRenderer render;



    private int kernelComputeGaussianRandom;            //计算高斯随机数
    private int kernelCreateHeightSpectrum;             //创建高度频谱
    private int kernelCreateDisplaceSpectrum;           //创建偏移频谱
    private int kernelFFTHorizontal;                    //FFT横向
    private int kernelFFTHorizontalEnd;                 //FFT横向，最后阶段
    private int kernelFFTVertical;                      //FFT纵向
    private int kernelFFTVerticalEnd;                   //FFT纵向,最后阶段
    private int kernelTextureGenerationDisplace;        //生成偏移纹理
    private int kernelTextureGenerationNormalBubbles;   //生成法线和泡沫纹理
    private RenderTexture GaussianRandomRT;             //高斯随机数
    private RenderTexture HeightSpectrumRT;             //高度频谱
    private RenderTexture DisplaceXSpectrumRT;          //X偏移频谱
    private RenderTexture DisplaceZSpectrumRT;          //Z偏移频谱
    private RenderTexture DisplaceRT;                   //偏移频谱
    private RenderTexture OutputRT;                     //临时储存输出纹理
    private RenderTexture NormalRT;                     //法线纹理
    private RenderTexture BubblesRT;                    //泡沫纹理

    private void Awake()
    {
        //添加网格及渲染组件
        filetr = gameObject.GetComponent<MeshFilter>();
        if (filetr == null)
        {
            filetr = gameObject.AddComponent<MeshFilter>();
        }
        render = gameObject.GetComponent<MeshRenderer>();
        if (render == null)
        {
            render = gameObject.AddComponent<MeshRenderer>();
        }
        mesh = new Mesh();
        filetr.mesh = mesh;
        render.material = OceanMaterial;
    }

    private void Start()
    {
        //创建网格
        CreateMesh();
        //初始化ComputerShader相关数据
        InitializeCSvalue();
    }
    private void Update()
    {
        time += Time.deltaTime * TimeScale;
        //计算海洋数据
        ComputeOceanValue();
    }


    /// <summary>
    /// 初始化Computer Shader相关数据
    /// </summary>
    private void InitializeCSvalue()
    {
        fftSize = (int)Mathf.Pow(2, FFTPow);

        //创建渲染纹理
        if (GaussianRandomRT != null && GaussianRandomRT.IsCreated())
        {
            GaussianRandomRT.Release();
            HeightSpectrumRT.Release();
            DisplaceXSpectrumRT.Release();
            DisplaceZSpectrumRT.Release();
            DisplaceRT.Release();
            OutputRT.Release();
            NormalRT.Release();
            BubblesRT.Release();
        }
        GaussianRandomRT = CreateRT(fftSize);
        HeightSpectrumRT = CreateRT(fftSize);
        DisplaceXSpectrumRT = CreateRT(fftSize);
        DisplaceZSpectrumRT = CreateRT(fftSize);
        DisplaceRT = CreateRT(fftSize);
        OutputRT = CreateRT(fftSize);
        NormalRT = CreateRT(fftSize);
        BubblesRT = CreateRT(fftSize);

        //获取所有kernelID
        kernelComputeGaussianRandom = OceanCS.FindKernel("ComputeGaussianRandom");
        kernelCreateHeightSpectrum = OceanCS.FindKernel("CreateHeightSpectrum");
        kernelCreateDisplaceSpectrum = OceanCS.FindKernel("CreateDisplaceSpectrum");
        kernelFFTHorizontal = OceanCS.FindKernel("FFTHorizontal");
        kernelFFTHorizontalEnd = OceanCS.FindKernel("FFTHorizontalEnd");
        kernelFFTVertical = OceanCS.FindKernel("FFTVertical");
        kernelFFTVerticalEnd = OceanCS.FindKernel("FFTVerticalEnd");
        kernelTextureGenerationDisplace = OceanCS.FindKernel("TextureGenerationDisplace");
        kernelTextureGenerationNormalBubbles = OceanCS.FindKernel("TextureGenerationNormalBubbles");

        //设置ComputerShader数据
        OceanCS.SetInt("N", fftSize);
        OceanCS.SetFloat("OceanLength", MeshLength);


        //生成高斯随机数
        OceanCS.SetTexture(kernelComputeGaussianRandom, "GaussianRandomRT", GaussianRandomRT);
        OceanCS.Dispatch(kernelComputeGaussianRandom, fftSize / 8, fftSize / 8, 1);

    }
    /// <summary>
    /// 计算海洋数据
    /// </summary>
    private void ComputeOceanValue()
    {
        OceanCS.SetFloat("A", A);
        WindAndSeed.z = Random.Range(1, 10f);
        WindAndSeed.w = Random.Range(1, 10f);
        Vector2 wind = new Vector2(WindAndSeed.x, WindAndSeed.y);
        wind.Normalize();
        wind *= WindScale;
        OceanCS.SetVector("WindAndSeed", new Vector4(wind.x, wind.y, WindAndSeed.z, WindAndSeed.w));
        OceanCS.SetFloat("Time", time);
        OceanCS.SetFloat("Lambda", Lambda);
        OceanCS.SetFloat("HeightScale", HeightScale);
        OceanCS.SetFloat("BubblesScale", BubblesScale);
        OceanCS.SetFloat("BubblesThreshold",BubblesThreshold);

        //生成高度频谱
        OceanCS.SetTexture(kernelCreateHeightSpectrum, "GaussianRandomRT", GaussianRandomRT);
        OceanCS.SetTexture(kernelCreateHeightSpectrum, "HeightSpectrumRT", HeightSpectrumRT);
        OceanCS.Dispatch(kernelCreateHeightSpectrum, fftSize / 8, fftSize / 8, 1);

        //生成偏移频谱
        OceanCS.SetTexture(kernelCreateDisplaceSpectrum, "HeightSpectrumRT", HeightSpectrumRT);
        OceanCS.SetTexture(kernelCreateDisplaceSpectrum, "DisplaceXSpectrumRT", DisplaceXSpectrumRT);
        OceanCS.SetTexture(kernelCreateDisplaceSpectrum, "DisplaceZSpectrumRT", DisplaceZSpectrumRT);
        OceanCS.Dispatch(kernelCreateDisplaceSpectrum, fftSize / 8, fftSize / 8, 1);


        if (ControlM == 0)
        {
            SetMaterialTex();
            return;
        }

        //进行横向FFT
        for (int m = 1; m <= FFTPow; m++)
        {
            int ns = (int)Mathf.Pow(2, m - 1);
            OceanCS.SetInt("Ns", ns);
            //最后一次进行特殊处理
            if (m != FFTPow)
            {
                ComputeFFT(kernelFFTHorizontal, ref HeightSpectrumRT);
                ComputeFFT(kernelFFTHorizontal, ref DisplaceXSpectrumRT);
                ComputeFFT(kernelFFTHorizontal, ref DisplaceZSpectrumRT);
            }
            else
            {
                ComputeFFT(kernelFFTHorizontalEnd, ref HeightSpectrumRT);
                ComputeFFT(kernelFFTHorizontalEnd, ref DisplaceXSpectrumRT);
                ComputeFFT(kernelFFTHorizontalEnd, ref DisplaceZSpectrumRT);
            }
            if (isControlH && ControlM == m)
            {
                SetMaterialTex();
                return;
            }
        }
        //进行纵向FFT
        for (int m = 1; m <= FFTPow; m++)
        {
            int ns = (int)Mathf.Pow(2, m - 1);
            OceanCS.SetInt("Ns", ns);
            //最后一次进行特殊处理
            if (m != FFTPow)
            {
                ComputeFFT(kernelFFTVertical, ref HeightSpectrumRT);
                ComputeFFT(kernelFFTVertical, ref DisplaceXSpectrumRT);
                ComputeFFT(kernelFFTVertical, ref DisplaceZSpectrumRT);
            }
            else
            {
                ComputeFFT(kernelFFTVerticalEnd, ref HeightSpectrumRT);
                ComputeFFT(kernelFFTVerticalEnd, ref DisplaceXSpectrumRT);
                ComputeFFT(kernelFFTVerticalEnd, ref DisplaceZSpectrumRT);
            }
            if (!isControlH && ControlM == m)
            {
                SetMaterialTex();
                return;
            }
        }

        //计算纹理偏移
        OceanCS.SetTexture(kernelTextureGenerationDisplace, "HeightSpectrumRT", HeightSpectrumRT);
        OceanCS.SetTexture(kernelTextureGenerationDisplace, "DisplaceXSpectrumRT", DisplaceXSpectrumRT);
        OceanCS.SetTexture(kernelTextureGenerationDisplace, "DisplaceZSpectrumRT", DisplaceZSpectrumRT);
        OceanCS.SetTexture(kernelTextureGenerationDisplace, "DisplaceRT", DisplaceRT);
        OceanCS.Dispatch(kernelTextureGenerationDisplace, fftSize / 8, fftSize / 8, 1);

        //生成法线和泡沫纹理
        OceanCS.SetTexture(kernelTextureGenerationNormalBubbles, "DisplaceRT", DisplaceRT);
        OceanCS.SetTexture(kernelTextureGenerationNormalBubbles, "NormalRT", NormalRT);
        OceanCS.SetTexture(kernelTextureGenerationNormalBubbles, "BubblesRT", BubblesRT);
        OceanCS.Dispatch(kernelTextureGenerationNormalBubbles, fftSize / 8, fftSize / 8, 1);

        SetMaterialTex();
    }

    /// <summary>
    /// 创建网格
    /// </summary>
    private void CreateMesh()
    {
        //fftSize = (int)Mathf.Pow(2, FFTPow);
        vertIndexs = new int[(MeshSize - 1) * (MeshSize - 1) * 6];
        positions = new Vector3[MeshSize * MeshSize];
        uvs = new Vector2[MeshSize * MeshSize];

        int inx = 0;
        for (int i = 0; i < MeshSize; i++)
        {
            for (int j = 0; j < MeshSize; j++)
            {
                int index = i * MeshSize + j;
                positions[index] = new Vector3((j - MeshSize / 2.0f) * MeshLength / MeshSize, 0, (i - MeshSize / 2.0f) * MeshLength / MeshSize);
                uvs[index] = new Vector2(j / (MeshSize - 1.0f), i / (MeshSize - 1.0f));

                if (i != MeshSize - 1 && j != MeshSize - 1)
                {
                    vertIndexs[inx++] = index;
                    vertIndexs[inx++] = index + MeshSize;
                    vertIndexs[inx++] = index + MeshSize + 1;

                    vertIndexs[inx++] = index;
                    vertIndexs[inx++] = index + MeshSize + 1;
                    vertIndexs[inx++] = index + 1;
                }
            }
        }
        mesh.vertices = positions;
        mesh.SetIndices(vertIndexs, MeshTopology.Triangles, 0);
        mesh.uv = uvs;
    }

    //创建渲染纹理
    private RenderTexture CreateRT(int size)
    {
        RenderTexture rt = new RenderTexture(size, size, 0, RenderTextureFormat.ARGBFloat);
        rt.enableRandomWrite = true;
        rt.Create();
        return rt;
    }
    //计算fft
    private void ComputeFFT(int kernel, ref RenderTexture input)
    {
        OceanCS.SetTexture(kernel, "InputRT", input);
        OceanCS.SetTexture(kernel, "OutputRT", OutputRT);
        OceanCS.Dispatch(kernel, fftSize / 8, fftSize / 8, 1);

        //交换输入输出纹理
        RenderTexture rt = input;
        input = OutputRT;
        OutputRT = rt;
    }
    //设置材质纹理
    private void SetMaterialTex()
    {
        //设置海洋材质纹理
        OceanMaterial.SetTexture("_Displace", DisplaceRT);
        OceanMaterial.SetTexture("_Normal", NormalRT);
        OceanMaterial.SetTexture("_Bubbles", BubblesRT);

        //设置显示纹理
        DisplaceXMat.SetTexture("_MainTex", DisplaceXSpectrumRT);
        DisplaceYMat.SetTexture("_MainTex", HeightSpectrumRT);
        DisplaceZMat.SetTexture("_MainTex", DisplaceZSpectrumRT);
        DisplaceMat.SetTexture("_MainTex", DisplaceRT);
        NormalMat.SetTexture("_MainTex", NormalRT);
        BubblesMat.SetTexture("_MainTex", BubblesRT);
    }
}
