package org.fieldtak.hub.ui

import android.Manifest
import android.content.pm.PackageManager
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.camera.core.CameraSelector
import androidx.camera.core.ExperimentalGetImage
import androidx.camera.core.ImageAnalysis
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.view.PreviewView
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import org.fieldtak.hub.R
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.content.ContextCompat
import androidx.lifecycle.compose.LocalLifecycleOwner
import com.google.mlkit.vision.barcode.BarcodeScannerOptions
import com.google.mlkit.vision.barcode.BarcodeScanning
import com.google.mlkit.vision.barcode.common.Barcode
import com.google.mlkit.vision.common.InputImage
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean

@OptIn(ExperimentalGetImage::class)
@Composable
fun QrScanner(onValue:(String)->Unit){
  val context=LocalContext.current
  val lifecycle=LocalLifecycleOwner.current
  var granted by remember { mutableStateOf(ContextCompat.checkSelfPermission(context, Manifest.permission.CAMERA)==PackageManager.PERMISSION_GRANTED) }
  val permissionLauncher=rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()){granted=it}
  LaunchedEffect(Unit){ if(!granted) permissionLauncher.launch(Manifest.permission.CAMERA) }

  if(!granted){ Box(Modifier.fillMaxSize(),contentAlignment=Alignment.Center){Text(stringResource(R.string.camera_permission_required))}; return }

  val providerFuture=remember(context){ProcessCameraProvider.getInstance(context)}
  val executor=remember{Executors.newSingleThreadExecutor()}
  val scanner=remember{BarcodeScanning.getClient(BarcodeScannerOptions.Builder().setBarcodeFormats(Barcode.FORMAT_QR_CODE).build())}
  DisposableEffect(Unit){
    onDispose{
      runCatching{if(providerFuture.isDone) providerFuture.get().unbindAll()}
      scanner.close(); executor.shutdown()
    }
  }

  PickQrFromGalleryButton(onQrDecoded = onValue)
    AndroidView(modifier=Modifier.fillMaxSize(), factory={ctx ->
    val view=PreviewView(ctx)
    providerFuture.addListener({
      val provider=providerFuture.get()
      val preview=androidx.camera.core.Preview.Builder().build().also{it.setSurfaceProvider(view.surfaceProvider)}
      val busy=AtomicBoolean(false); val delivered=AtomicBoolean(false)
      val analysis=ImageAnalysis.Builder().setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST).build()
      analysis.setAnalyzer(executor){ proxy ->
        val media=proxy.image
        if(media==null||delivered.get()||!busy.compareAndSet(false,true)){proxy.close();return@setAnalyzer}
        scanner.process(InputImage.fromMediaImage(media,proxy.imageInfo.rotationDegrees))
          .addOnSuccessListener{codes->codes.firstOrNull()?.rawValue?.let{v->if(delivered.compareAndSet(false,true)) onValue(v)}}
          .addOnCompleteListener{busy.set(false);proxy.close()}
      }
      provider.unbindAll(); provider.bindToLifecycle(lifecycle,CameraSelector.DEFAULT_BACK_CAMERA,preview,analysis)
    },ContextCompat.getMainExecutor(ctx))
    view
  })
}
