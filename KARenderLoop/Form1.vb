Imports SharpDX
Imports SharpDX.Direct3D
Imports SharpDX.Direct3D11
Imports SharpDX.DXGI
Imports SharpDX.Mathematics.Interop
Imports System.Threading

Public Class KARenderLoop

    ' GPU Load Control
    Private device As SharpDX.Direct3D11.Device
    Private renderLoopThread As Thread
    Private isRunning As Boolean = False
    Private loadLevel As Integer = 0 ' 0=Off, 1=Low, 2=Medium, 3=High

    ' System Tray
    Private WithEvents notifyIcon As New NotifyIcon()
    Private WithEvents contextMenu As New ContextMenuStrip()

    Private Class NativeMethods
        <Runtime.InteropServices.DllImport("user32.dll")>
        Public Shared Function SetWindowPos(hWnd As IntPtr, hWndInsertAfter As IntPtr, X As Integer, Y As Integer, cx As Integer, cy As Integer, uFlags As UInteger) As Boolean
        End Function
    End Class

    Private Sub Form1_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        ' Hide the main form (runs in background)
        Me.WindowState = FormWindowState.Minimized
        Me.ShowInTaskbar = False

        ' Initialize system tray icon
        notifyIcon.Icon = Me.Icon ' (Add an icon to Resources)
        notifyIcon.Text = "GPU K-ARL"
        notifyIcon.Visible = True

        ' Create context menu
        Dim offTool = New ToolStripMenuItem("Off", Nothing, AddressOf SetLoadLevel)
        offTool.Tag = 0
        Dim lowTool = New ToolStripMenuItem("Low", Nothing, AddressOf SetLoadLevel)
        lowTool.Tag = 1
        Dim mediumTool = New ToolStripMenuItem("Medium", Nothing, AddressOf SetLoadLevel)
        mediumTool.Tag = 2
        Dim highTool = New ToolStripMenuItem("High", Nothing, AddressOf SetLoadLevel)
        highTool.Tag = 3
        Dim exitTool = New ToolStripMenuItem("Exit", Nothing, AddressOf ExitApp)

        contextMenu.Items.AddRange({offTool, lowTool, mediumTool, highTool, New ToolStripSeparator(), exitTool})
        notifyIcon.ContextMenuStrip = contextMenu

        ' Start with Low load by default
        SetLoadLevel(Nothing, New EventArgs())
    End Sub

    Private Sub SetLoadLevel(sender As Object, e As EventArgs)
        If sender IsNot Nothing Then
            loadLevel = CInt(DirectCast(sender, ToolStripMenuItem).Tag)
        End If

        ' Update menu check state
        For Each item In contextMenu.Items.OfType(Of ToolStripMenuItem)()
            If item.Tag IsNot Nothing Then
                item.Checked = (CInt(item.Tag) = loadLevel)
            End If
        Next

        ' Start/Stop the render thread
        If loadLevel > 0 AndAlso Not isRunning Then
            StartRenderLoop()
        ElseIf loadLevel = 0 AndAlso isRunning Then
            StopRenderLoop()
        End If

        notifyIcon.Text = "GPU K-ARL - " & GetLoadLevelName(loadLevel)
    End Sub

    Private Function GetLoadLevelName(level As Integer) As String
        Select Case level
            Case 0 : Return "Off"
            Case 1 : Return "Low"
            Case 2 : Return "Medium"
            Case 3 : Return "High"
            Case Else : Return "Unknown"
        End Select
    End Function

    Private Sub StartRenderLoop()
        If isRunning Then Return

        isRunning = True
        renderLoopThread = New Thread(AddressOf RenderLoop)
        renderLoopThread.IsBackground = True
        renderLoopThread.Start()
    End Sub

    Private Sub StopRenderLoop()
        isRunning = False
        If renderLoopThread IsNot Nothing AndAlso renderLoopThread.IsAlive Then
            renderLoopThread.Join(500)
        End If
        device?.Dispose()
        device = Nothing
    End Sub

    Private Sub RenderLoop()
        ' Initialize Direct3D
        device = New SharpDX.Direct3D11.Device(DriverType.Hardware, DeviceCreationFlags.None)

        ' Create hidden render form
        Dim renderForm = New Form() With {
            .Width = 64,
            .Height = 64,
            .ShowInTaskbar = False,
            .Opacity = 0.001,
            .FormBorderStyle = FormBorderStyle.None
        }
        NativeMethods.SetWindowPos(renderForm.Handle, IntPtr.Zero, -10000, -10000, 0, 0, &H80)
        renderForm.Show()

        ' Create swap chain
        Dim swapChainDesc As New SwapChainDescription() With {
            .BufferCount = 1,
            .ModeDescription = New ModeDescription(64, 64, New Rational(60, 1), Format.R8G8B8A8_UNorm),
            .IsWindowed = True,
            .OutputHandle = renderForm.Handle,
            .SampleDescription = New SampleDescription(1, 0),
            .SwapEffect = SwapEffect.Discard,
            .Usage = Usage.RenderTargetOutput
        }

        Dim factory As New DXGI.Factory1()
        Dim swapChain As New SwapChain(factory, device, swapChainDesc)

        ' Create render target
        Dim backBuffer = Texture2D.FromSwapChain(Of Texture2D)(swapChain, 0)
        Dim renderTarget = New RenderTargetView(device, backBuffer)

        ' Create depth buffer
        Dim depthBuffer = New Texture2D(device, New Texture2DDescription() With {
            .Width = 64,
            .Height = 64,
            .MipLevels = 1,
            .ArraySize = 1,
            .Format = Format.D16_UNorm,
            .SampleDescription = New SampleDescription(1, 0),
            .Usage = ResourceUsage.Default,
            .BindFlags = BindFlags.DepthStencil
        })
        Dim depthView = New DepthStencilView(device, depthBuffer)

        ' Set viewport
        device.ImmediateContext.Rasterizer.SetViewport(New Viewport(0, 0, 64, 64, 0.0F, 1.0F))

        ' Main render loop
        Dim angle As Single = 0.0F
        While isRunning
            ' Clear buffers
            device.ImmediateContext.ClearRenderTargetView(renderTarget, New RawColor4(0, 0, 0, 1))
            device.ImmediateContext.ClearDepthStencilView(depthView, DepthStencilClearFlags.Depth, 1.0F, 0)

            ' Update rotation
            angle += 0.01F * loadLevel

            ' Present (no actual drawing needed for GPU load)
            swapChain.Present(0, PresentFlags.None)

            ' Adjust workload
            Select Case loadLevel
                Case 1 : Thread.Sleep(8) ' ~120 FPS
                Case 2 : Thread.Sleep(0.8)  ' ~1000 FPS
                    ' Case 3 : No sleep (max load)
            End Select
            My.Application.DoEvents()
        End While

        ' Cleanup
        depthView.Dispose()
        depthBuffer.Dispose()
        renderTarget.Dispose()
        backBuffer.Dispose()
        swapChain.Dispose()
        factory.Dispose()
        renderForm.Invoke(Sub() renderForm.Close())
    End Sub

    Private Sub ExitApp(sender As Object, e As EventArgs)
        StopRenderLoop()
        notifyIcon.Visible = False
        Application.Exit()
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        If e.CloseReason = CloseReason.UserClosing Then
            e.Cancel = True ' Prevent closing (only exit via tray menu)
            Me.WindowState = FormWindowState.Minimized
        End If
    End Sub

End Class
